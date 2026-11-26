using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Inspect a complete street child visit to the modular park swings.")]
        [PrebuildSetup(typeof(CityStreetChildAssetsSetup))]
        [Timeout(240000)]
        public IEnumerator CityParkPlaygroundChildVisit()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.PrepareHomeReturn();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds * 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);

            CityPlaygroundSwing[] seats = city.World.PlaygroundSwings;
            Assert.That(seats, Has.Length.EqualTo(2));
            CityPlaygroundSwing first = seats[0];
            Transform frame = first.transform.parent;
            Assert.That(Vector3.Distance(first.Body.position, seats[1].Body.position), Is.EqualTo(1.96f).Within(.02f));
            Assert.That(first.SeatCenter.y - first.GroundCenter.y, Is.EqualTo(.62f).Within(.02f));
            bool benchFound = false;
            foreach (CityBenchSitInteraction bench in city.BenchSits)
                benchFound |= (bench.transform.position - frame.position).sqrMagnitude < 30f;
            Assert.That(benchFound, Is.True);

            Camera camera = city.Camera;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            float previousDelta = Time.captureDeltaTime;
            bool wasFollowing = follow != null && follow.enabled;
            city.Player.Motor.SetInputEnabled(false);
            Vector3 right = first.RestRotation * Vector3.right;
            Vector3 viewingPoint = frame.position + first.RestForward * 4.6f + right * 4f;
            city.Player.Motor.Teleport(viewingPoint + right * 2.7f);
            if (follow != null) follow.enabled = false;
            camera.transform.position = viewingPoint + Vector3.up * 1.55f;
            camera.transform.rotation = Quaternion.LookRotation(frame.position + Vector3.up * 1.26f - camera.transform.position);
            try
            {
                Time.captureDeltaTime = .06f;
                Physics.SyncTransforms();
                int slot = -1;
                int encounter = 0;
                CityPedestrianPresentation child = null;
                CityPlaygroundSwing rode = null;
                Vector3 departure = default, returned = default;
                bool rodeInFrame = false, dismounted = false, resumedRoute = false;
                float peakAngle = 0f;
                deadline = Time.realtimeSinceStartup + 105f;
                while (Time.realtimeSinceStartup < deadline && !resumedRoute)
                {
                    var ambient = city.AmbientPedestrians;
                    if (slot < 0)
                    {
                        for (int i = 0; i < CityAmbientPedestrians.MaximumActive; i++)
                        {
                            if (ambient.GetPlaygroundPhase(i) != CityPlaygroundVisit.Phase.Approach) continue;
                            slot = i;
                            child = ambient.GetWalkerBody(i);
                            ambient.TryGetWalker(i, out departure, out _, out encounter);
                            rode = seats[0].Rider == encounter ? seats[0] : seats[1];
                            Assert.That(rode.TryReserve(encounter + 10000), Is.False);
                            break;
                        }
                    }
                    else
                    {
                        Assert.That(ambient.GetWalkerBody(slot), Is.SameAs(child));
                        Assert.That(ambient.TryGetWalker(slot, out Vector3 position, out _, out int current), Is.True);
                        Assert.That(current, Is.EqualTo(encounter));
                        var phase = ambient.GetPlaygroundPhase(slot);
                        if (phase == CityPlaygroundVisit.Phase.Ride)
                        {
                            peakAngle = Mathf.Max(peakAngle, Mathf.Abs(rode.Angle));
                            if (!rodeInFrame && peakAngle > 7f)
                            {
                                // The coroutine runs before the ambient LateUpdate.
                                // Sample this frame's physics pose before comparing contacts.
                                ambient.Advance(.0001f);
                                float pelvisError = Vector3.Distance(child.Registry.PelvisAnchor.position, rode.Pelvis.position);
                                CheckGrip("SOCKET_Grip.L", rode.LeftGrip.position, out float leftGripError);
                                CheckGrip("SOCKET_Grip.R", rode.RightGrip.position, out float rightGripError);
                                CaptureCurrentCamera(camera, "CityParkPlaygroundChildVisit", "01-riding", 960, 720);
                                Assert.That(pelvisError, Is.LessThan(.025f), "Pelvis support");
                                Assert.That(leftGripError, Is.LessThan(.025f), "Left suspension");
                                Assert.That(rightGripError, Is.LessThan(.025f), "Right suspension");
                                rodeInFrame = true;
                            }
                        }
                        if (!dismounted && phase == CityPlaygroundVisit.Phase.Leave)
                        {
                            Assert.That(child.AuthoredActionWeight, Is.Zero);
                            CaptureCurrentCamera(camera, "CityParkPlaygroundChildVisit", "02-grounded-exit", 960, 720);
                            dismounted = true;
                        }
                        if (phase == CityPlaygroundVisit.Phase.None)
                        {
                            Assert.That(dismounted && rode.Rider != encounter, Is.True);
                            if (returned == default)
                            {
                                Assert.That(Vector3.Distance(position, departure), Is.LessThan(.6f));
                                returned = position;
                            }
                            resumedRoute = child.IsMoving && Vector3.Distance(position, returned) > .6f;
                        }
                    }
                    yield return null;
                }
                Assert.That(resumedRoute && rodeInFrame, Is.True,
                    $"visit slot={slot}, phase={(slot < 0 ? CityPlaygroundVisit.Phase.None : city.AmbientPedestrians.GetPlaygroundPhase(slot))}, arc={peakAngle:F2}");

                void CheckGrip(string boneName, Vector3 grip, out float error)
                {
                    foreach (Transform bone in child.Registry.Animator.GetComponentsInChildren<Transform>())
                        if (bone.name == boneName)
                        {
                            error = Vector3.Distance(bone.position, grip);
                            return;
                        }
                    error = float.PositiveInfinity;
                    Assert.Fail("Missing " + boneName);
                }
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                city.Player.Motor.SetInputEnabled(true);
                if (follow != null) follow.enabled = wasFollowing;
            }
        }
    }
}
