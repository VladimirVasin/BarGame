using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class EastGuardAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.EastGuardAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        private IEnumerator VerifyEastGuards(CityGameRoot city, Camera camera, CityMapCityTeleportGround landing)
        {
            CityEastGuardController guards = city.EastGuards;
            Assert.That(guards, Is.Not.Null);
            Assert.That(guards.IsInitialized, Is.True);
            guards.AutoAdvance = false;
            var motor = city.Player.Motor;
            bool interactorEnabled = city.Player.Interactor.InputEnabled;
            Vector3 checkpoint = city.World.EastExitPlan.CheckpointPosition;
            float streetCenterX = city.World.EastExitPlan.ApproachStart.x - city.Layout.RoadWidth * .5f;
            Vector2 waitingPoint = new Vector2(streetCenterX, checkpoint.z);
            Vector2 pairedWatchPoint = new Vector2(Mathf.Max(streetCenterX, checkpoint.x - 16f), checkpoint.z);
            city.Player.Interactor.SetInputEnabled(true);
            try
            {
                Stand(waitingPoint.x, waitingPoint.y);
                for (int i = 0; i < 2; i++)
                {
                    EastGuardActor actor = guards.Actor(i);
                    Assert.That(actor.Height, Is.EqualTo(i == 0 ? 1.82f : 1.90f).Within(.015f));
                    Assert.That(actor.Wardrobe.CurrentOutfitId, Is.EqualTo(EastGuardAssetProvider.OutfitId(i)));
                    Assert.That(actor.Wardrobe.Owns(actor.FaceRenderer), Is.False);
                    Assert.That(actor.RifleRoot.GetComponentsInChildren<Collider>(), Is.Empty);
                    Assert.That(actor.RifleRoot.GetComponentsInChildren<Rigidbody>(), Is.Empty);
                    foreach (Renderer renderer in actor.RifleRoot.GetComponentsInChildren<Renderer>())
                        Assert.That(actor.Wardrobe.Owns(renderer), Is.False, "A rifle is separate equipment, not anatomy or clothing.");
                    AssertCarry(actor);
                    AssertGround(actor);
                    yield return Frame(actor, 3.4f, .15f, 1.0f, "guard-" + i + "-01-standing-night");
                    yield return Frame(actor, 1.5f, .05f, 1.60f, "guard-" + i + "-02-face-night");
                }
                Assert.That(guards.Actor(0).Motion.Animator.avatar, Is.Not.SameAs(guards.Actor(1).Motion.Animator.avatar));
                Assert.That(guards.Actor(0).RifleRoot, Is.Not.SameAs(guards.Actor(1).RifleRoot));

                int initialPatrols = guards.Duty.CompletedPatrols;
                var photographed = new bool[2];
                bool checkedBlocking = false;
                for (int step = 0; step < 1900 && guards.Duty.CompletedPatrols < initialPatrols + 2; step++)
                {
                    Physics.SyncTransforms();
                    guards.Advance(.1f);
                    Assert.That(guards.Duty.IsHome(0) || guards.Duty.IsHome(1), Is.True,
                        "Both guards cannot leave the checkpoint at the same time.");
                    for (int i = 0; i < 2; i++)
                    {
                        EastGuardActor actor = guards.Actor(i);
                        if (guards.Duty.IsHome(i))
                            Assert.That(Vector3.Distance(actor.transform.position,
                                guards.Plan.Station(i) + Vector3.up * actor.GroundOffset), Is.LessThan(.1f));
                        if (step % 20 == 0) { AssertCarry(actor); AssertGround(actor); }
                        if (!photographed[i] && guards.Speed(i) > .3f &&
                            Vector3.Distance(actor.transform.position, guards.Plan.Station(i)) > 2f)
                        {
                            yield return Frame(actor, 2.8f, 1.7f, 1.0f, "guard-" + i + "-03-patrol");
                            photographed[i] = true;
                            if (!checkedBlocking)
                            {
                                Vector3 before = actor.transform.position;
                                Vector3 block = before + actor.transform.forward * 1.4f;
                                Stand(block.x, block.z);
                                for (int wait = 0; wait < 25; wait++) { Physics.SyncTransforms(); guards.Advance(.1f); }
                                Assert.That(guards.IsBlocked, Is.True, "A visitor standing in the patrol route must make the guard wait.");
                                Assert.That(Vector3.Distance(before, actor.transform.position), Is.LessThan(1f));
                                Stand(waitingPoint.x, waitingPoint.y);
                                checkedBlocking = true;
                            }
                        }
                    }
                    if (step % 100 == 0) yield return null;
                }
                Assert.That(guards.Duty.CompletedPatrols, Is.GreaterThanOrEqualTo(initialPatrols + 2),
                    "A physical patrol failed to return: " + guards.Duty.Phase + "/" + guards.Duty.Waypoint + "; blocked=" + guards.IsBlocked);
                Assert.That(photographed[0] && photographed[1], Is.True);

                double pausedTime = guards.LifeSeconds;
                Vector3 pausedPosition = guards.Actor(0).transform.position;
                Quaternion pausedHead = guards.Actor(0).Motion.Head.rotation;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    guards.Advance(.7f);
                    yield return null;
                    Assert.That(guards.LifeSeconds, Is.EqualTo(pausedTime));
                    Assert.That(guards.Actor(0).transform.position, Is.EqualTo(pausedPosition));
                    Assert.That(guards.Actor(0).Motion.Head.rotation, Is.EqualTo(pausedHead));
                }

                // Both readers use the actual E entry and the shared speech view.
                for (int i = 0; i < 2; i++)
                {
                    EastGuardActor actor = guards.Actor(i);
                    Vector3 near = actor.transform.position + actor.transform.forward * 1.6f;
                    Stand(near.x, near.z);
                    Assert.That(guards.RequestReply(i, city.Player.Interactor), Is.True);
                    Assert.That(guards.RequestReply(i, city.Player.Interactor), Is.False, "Repeated E cannot queue another reply.");
                    for (int step = 0; step < 3; step++) guards.Advance(.1f);
                    Assert.That(guards.Bubbles.IsShowing(actor.Motion), Is.True);
                    Assert.That(guards.LastSpeaker, Is.EqualTo(i));
                    Assert.That(guards.LastLineKey, Does.StartWith("city.east.guard." + (i == 0 ? "senior" : "junior") + ".reply."));
                    Assert.That(guards.Bubbles.TryGetSpeechFaceSample(actor.Motion, out _), Is.True);
                    yield return Frame(actor, 1.8f, .15f, 1.55f, "guard-" + i + "-04-reply-face");
                    for (int step = 0; step < 65; step++) guards.Advance(.1f);
                }

                // Observe a whole pair from the public approach; it cannot be interrupted by queued E.
                Stand(pairedWatchPoint.x, pairedWatchPoint.y);
                int exchanges = guards.CompletedExchanges;
                bool sawPair = false;
                for (int step = 0; step < 1300 && guards.CompletedExchanges == exchanges; step++)
                {
                    Physics.SyncTransforms(); guards.Advance(.1f);
                    if (!guards.LastLineKey.Contains(".pair.") || !guards.HasExchange || sawPair) continue;
                    sawPair = true;
                    camera.transform.SetPositionAndRotation(new Vector3(pairedWatchPoint.x,
                        guards.Plan.GroundTop(pairedWatchPoint) + EyeHeight, pairedWatchPoint.y),
                        Quaternion.LookRotation(new Vector3(1f, 0f, 0f)));
                    camera.fieldOfView = 65f;
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.City, "guards-05-paired-watch");
                    string currentLine = guards.LastLineKey;
                    int currentSpeaker = guards.LastSpeaker;
                    Vector3 nearby = guards.Actor(0).transform.position + guards.Actor(0).transform.forward * 1.6f;
                    Stand(nearby.x, nearby.z);
                    Assert.That(guards.RequestReply(0, city.Player.Interactor), Is.True);
                    Assert.That(guards.PendingReply, Is.EqualTo(0));
                    for (int frame = 0; frame < 3; frame++) guards.Advance(.1f);
                    Assert.That(guards.LastLineKey, Is.EqualTo(currentLine), "E must not replace the active paired line.");
                    Assert.That(guards.Bubbles.IsShowing(guards.Actor(currentSpeaker).Motion), Is.True);
                }
                Assert.That(guards.CompletedExchanges, Is.GreaterThan(exchanges));
                Assert.That(guards.LastLineKey, Does.StartWith("city.east.guard.senior.reply."),
                    "The pending reply starts only after the partner has finished.");

                GameSessionState.AdvanceGameTime((float)((15d * 60d) / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                for (int i = 0; i < 2; i++)
                    yield return Frame(guards.Actor(i), 3.4f, -.2f, 1.0f, "guard-" + i + "-06-standing-day");
                guards.enabled = false;
                Assert.That(guards.Bubbles.IsShowing(guards.Actor(0).Motion), Is.False);
                Assert.That(guards.Bubbles.IsDeclared(guards.Actor(1).Motion), Is.False);
                guards.enabled = true;
                guards.Advance(.1f);
                Assert.That(guards.Bubbles.IsDeclared(guards.Actor(0).Motion), Is.True);
                for (int i = 0; i < 2; i++)
                    Assert.That(NpcFootstepSources.Prune(), Does.Contain(guards.Actor(i).transform));
                Debug.Log("EAST GUARDS: individual models, separate carried rifles, physical alternating patrols, shared speech, pause and cleanup verified.");
            }
            finally
            {
                guards.AutoAdvance = true;
                city.Player.Interactor.SetInputEnabled(interactorEnabled);
            }

            void Stand(float x, float z)
            {
                Assert.That(landing.TryResolveStandingPosition(new Vector2(x, z), out Vector3 ground), Is.True);
                motor.Teleport(ground);
                Physics.SyncTransforms();
            }
            IEnumerator Frame(EastGuardActor actor, float forward, float side, float height, string name)
            {
                Vector3 focus = actor.transform.position + Vector3.up * height;
                Vector3 eye = focus + actor.transform.forward * forward + actor.transform.right * side;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 48f;
                // Manual duty stepping can change many transforms inside one frame.
                // Let the normal renderer update sync rigid equipment with the rig
                // before Camera.Render reads the GPU Resident Drawer instance data.
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.City, name);
            }
            void AssertCarry(EastGuardActor actor)
            {
                Transform carry = CityPedestrianHandProps.FindSocket(actor.RifleRoot, "ANCHOR_Carry");
                Assert.That(carry, Is.Not.Null);
                Assert.That(Vector3.Distance(carry.position, actor.CarrySocket.position), Is.LessThan(.01f),
                    "The separate rifle lost its real shoulder attachment after animation.");
            }
            void AssertGround(EastGuardActor actor)
            {
                Assert.That(Physics.Raycast(actor.transform.position + Vector3.up * .15f, Vector3.down,
                    out RaycastHit ground, .5f, ~0, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(actor.transform.position.y - actor.GroundOffset, Is.EqualTo(ground.point.y).Within(.075f),
                    "The duty route must follow the actual standing surface, not just its plan.");
            }
        }
    }
}
