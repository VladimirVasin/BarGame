using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class BarInteriorSpawnPlayModeTests
    {
        [UnityTest]
        public IEnumerator VodkaService_ReturnsToSeatedViewAfterEmptyShot()
            => VerifyBottleService(DrinkId.Vodka, BarDrinkVesselKind.ShotGlass,
                verifyCompletionOnly: true);

        private static IEnumerator VerifyHeroDrinkCompletion(BarInteriorRoot bar,
            BarDrinkVesselView vessel)
        {
            BarDrinkShopController shop = bar.DrinkShop;
            BarPlayerDrinkReach reach = shop.PlayerDrinkReach;
            CounterSeatView view = shop.CounterSeatView;
            PlayerAnimatedInteractionController interaction = view.Seat.Controller;
            var probe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            Pose counter = new Pose(vessel.transform.position, vessel.transform.rotation);
            bool sawReturnContact = false;
            bool sawRestoredPose = false;
            float returnError = float.PositiveInfinity;
            float deadline = Time.realtimeSinceStartup + 20f;
            try
            {
                Assert.That(view.IsActionLookLocked, Is.True);
                while (shop.IsServing && Time.realtimeSinceStartup < deadline)
                    yield return AtFinalBarPose(probe, () =>
                    {
                        if (!reach.ReturnContactObserved) return;
                        sawReturnContact = true;
                        returnError = reach.LastReturnContactError;
                        sawRestoredPose |= reach.IsReturnPoseRestored;
                        Assert.That(Vector3.Distance(vessel.transform.position, counter.position),
                            Is.LessThanOrEqualTo(BarPlayerDrinkReach.ReleaseTolerance));
                    });
                yield return AtFinalBarPose(probe, () =>
                {
                    AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior,
                        "Test vodka return to seated view");
                    Assert.That(shop.IsServing, Is.False,
                        $"phase={shop.Phase} progress={shop.Timeline.PhaseProgress:F4}, " +
                        $"attached={reach.IsAttached}, carry={reach.CarryProgress:F4}, " +
                        $"release={reach.ReturnContactObserved}, restored={reach.IsReturnPoseRestored}, " +
                        $"support={reach.CounterSupportError:F6}, lastReturn={reach.LastReturnContactError:F6}, " +
                        $"wrist={reach.LastSolvedWristTargetError:F6}, grip={reach.GripError:F6}, " +
                        $"nested={interaction.IsNestedLoopActionActive}, lookLocked={view.IsActionLookLocked}");
                    Assert.That(shop.Phase, Is.EqualTo(BarDrinkServicePhase.EmptyOnCounter));
                    Assert.That(interaction.IsNestedLoopActionActive, Is.False);
                    Assert.That(view.IsActionLookLocked, Is.False);
                    Assert.That(view.IsFirstPerson, Is.True, "The normal seated view must remain available.");
                    Assert.That(sawReturnContact && sawRestoredPose, Is.True,
                        "The hand must release at the counter and finish retracting before the action completes.");
                    Assert.That(returnError, Is.LessThanOrEqualTo(BarPlayerDrinkReach.ReleaseTolerance));
                    Assert.That(shop.PurchaseCommitted, Is.False);
                });
            }
            finally { probe.CancelPending(); Object.Destroy(probe); }
        }

        [UnityTest]
        public IEnumerator CounterSeats_SideEntryExitKeepBodyOutsideFurniture()
        {
            BarInteriorRoot bar = null;
            yield return LoadBar(value => bar = value);
            bar.ArrivalPresentation.Skip();
            Assert.That(bar.CounterStations, Has.Count.EqualTo(3));
            var jacket = ((Player3DCharacterPresentation)bar.Player.Visual).Registry.GetComponent<PlayerJacketCloth>();
            Assert.That(jacket.ClothPhysicsEnabled, Is.False, "The jacket uses the global rig-only gameplay presentation.");
            jacket.enabled = false;
            jacket.enabled = true;
            Assert.That(jacket.ClothPhysicsEnabled, Is.False, "Re-enabling the component must keep jacket physics off.");
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool wasFollowing = follow.enabled;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            var finalPose = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
            var clock = new object();
            var clearance = new CounterSeatSkinClearance(bar);
            try
            {
                follow.enabled = false;
                for (int seatIndex = 0; seatIndex < bar.CounterStations.Count; seatIndex++)
                {
                    BarCounterStation station = bar.CounterStations[seatIndex];
                    CounterSeatPlan plan = station.Seat.Plan;
                    PlayerAnimatedInteractionController interaction = station.Seat.Controller;
                    bar.Player.Motor.Teleport(plan.ApproachWaypoints[0]);
                    bar.Player.GameObject.transform.rotation = plan.EntryPose.RootRotation;
                    Physics.SyncTransforms();
                    Assert.That(interaction.BeginExternalClock(clock), Is.True);
                    station.Interact(bar.Player.Interactor);
                    Assert.That(interaction.IsActive, Is.True);
                    string prefix = "seat-" + (seatIndex + 1);
                    void StageObserver()
                    {
                        camera.transform.SetPositionAndRotation(
                            plan.InteractionPosition + new Vector3(1.05f, .85f, -1.55f),
                            Quaternion.LookRotation(new Vector3(-1.05f, -.65f, 1.2f), Vector3.up));
                        camera.fieldOfView = 58f;
                    }
                    StageObserver();
                    yield return null;

                    int frames = 0;
                    bool sawEnter = false, sawSide = false, sawRaised = false;
                    int nextShot = 0;
                    float[] shotProgress = { 0f, .09f, .18f, .30f, .40f, .48f, .58f, .68f, .82f, .92f, .98f };
                    while (interaction.Phase != PlayerAnimatedInteractionPhase.Looping && frames++ < 300)
                    {
                        interaction.AdvanceExternal(clock, .05f);
                        yield return AtFinalBarPose(finalPose, () =>
                        {
                            StageObserver();
                            clearance.AssertClear(prefix + " enter " + interaction.PhaseProgress.ToString("F3"));
                            if (interaction.Phase != PlayerAnimatedInteractionPhase.Entering) return;
                            sawEnter = true;
                            Vector3 pelvis = ((Player3DCharacterPresentation)bar.Player.Visual).Registry.Anchors.Pelvis.position;
                            if (Mathf.Abs(pelvis.x - plan.ActionHipPosition.x) > .38f) sawSide = true;
                            if (pelvis.y > plan.ActionHipPosition.y + .025f) sawRaised = true;
                            if (nextShot < shotProgress.Length && interaction.PhaseProgress >= shotProgress[nextShot])
                            {
                                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                                    prefix + "-enter-" + nextShot);
                                nextShot++;
                            }
                        });
                    }
                    Assert.That(interaction.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
                    Assert.That(sawEnter && sawSide && sawRaised, Is.True,
                        "Entry must visibly pass beside the seat and lift before transferring onto it.");
                    yield return AtFinalBarPose(finalPose, () => clearance.AssertClear(prefix + " seated"));
                    float menuDeadline = Time.realtimeSinceStartup + TimeoutSeconds;
                    while (!bar.DrinkShop.CanRestPhysicalMenu &&
                           Time.realtimeSinceStartup < menuDeadline) yield return null;
                    Assert.That(bar.DrinkShop.RestPhysicalMenuAtCounter(), Is.True);
                    Assert.That(station.Seat.RequestExit(), Is.True);
                    StageObserver();
                    yield return null;
                    frames = 0;
                    nextShot = 0;
                    bool sawExit = false, sawExitSide = false;
                    while (interaction.IsActive && frames++ < 300)
                    {
                        interaction.AdvanceExternal(clock, .05f);
                        yield return AtFinalBarPose(finalPose, () =>
                        {
                            StageObserver();
                            clearance.AssertClear(prefix + " exit " + interaction.PhaseProgress.ToString("F3"));
                            if (interaction.Phase != PlayerAnimatedInteractionPhase.Exiting) return;
                            sawExit = true;
                            Vector3 pelvis = ((Player3DCharacterPresentation)bar.Player.Visual).Registry.Anchors.Pelvis.position;
                            if (Mathf.Abs(pelvis.x - plan.ActionHipPosition.x) > .38f) sawExitSide = true;
                            if (nextShot < shotProgress.Length && interaction.PhaseProgress >= shotProgress[nextShot])
                            {
                                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                                    prefix + "-exit-" + nextShot);
                                nextShot++;
                            }
                        });
                    }
                    Assert.That(interaction.IsActive, Is.False);
                    Assert.That(sawExit && sawExitSide, Is.True, "Exit must use the same side passage.");
                    Assert.That(Vector3.Distance(bar.Player.GameObject.transform.position, plan.ExitPose.RootPosition),
                        Is.LessThan(.025f), "The visible exit must finish at the independent grounded dock.");
                    interaction.ReleaseExternalClock(clock);
                    float closeDeadline = Time.realtimeSinceStartup + TimeoutSeconds;
                    while (bar.DrinkShop.IsOpen && Time.realtimeSinceStartup < closeDeadline)
                        yield return null;
                    Assert.That(bar.DrinkShop.IsOpen, Is.False);
                    Assert.That(bar.Player.Motor.InputEnabled, Is.True);
                    Assert.That(bar.Player.PresentationVisibility.IsHidden, Is.False);
                }
            }
            finally
            {
                finalPose.CancelPending(); Object.Destroy(finalPose);
                foreach (BarCounterStation station in bar.CounterStations)
                {
                    station.Seat.Cancel();
                    station.Seat.Controller.ReleaseExternalClock(clock);
                }
                clearance.Dispose();
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = fieldOfView;
                follow.enabled = wasFollowing;
            }
        }

        private sealed class CounterSeatSkinClearance : IDisposable
        {
            private sealed class BodyMesh
            {
                internal Renderer Renderer;
                internal SkinnedMeshRenderer Skin;
                internal Vector3[] StaticVertices;
                internal int[] Triangles;
            }

            private readonly List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid> furniture =
                new List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid>();
            private readonly List<BodyMesh> body = new List<BodyMesh>();
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid> nearby =
                new List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid>();
            private readonly Mesh baked = new Mesh();

            internal CounterSeatSkinClearance(BarInteriorRoot bar)
            {
                foreach (MeshFilter mesh in bar.Room.GetComponentsInChildren<MeshFilter>(true))
                {
                    string name = mesh.name;
                    if (!(name.StartsWith("Bar Stool ") || name.StartsWith("Bar Drink Service Stool") ||
                          name == "Counter Top" || name == "Counter Return Top" ||
                          name == "Counter Front Panels" || name == "Counter Return Panels" ||
                          name == "Counter Foot Rail" || name == "Counter Return Foot Rail" ||
                          name == "Bar Counter Return")) continue;
                    BarDrinkServiceWorldBuilderPlayModeTests.ReadSolidMesh(mesh.sharedMesh,
                        out Vector3[] points, out int[] triangles);
                    for (int vertex = 0; vertex < points.Length; vertex++)
                        points[vertex] = mesh.transform.TransformPoint(points[vertex]);
                    BarDrinkServiceWorldBuilderPlayModeTests.AddClosedPropPieces(furniture, name, points, triangles);
                }
                Assert.That(furniture.Count, Is.EqualTo(28),
                    "All five real stools, their feet/stems, counter tops, panels, rails and return must participate.");
                foreach (Renderer renderer in bar.Player.Visual.Renderers)
                {
                    SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
                    Mesh source = skin != null ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (source == null || renderer is ParticleSystemRenderer) continue;
                    BarDrinkServiceWorldBuilderPlayModeTests.ReadSolidMesh(source,
                        out Vector3[] points, out int[] triangles);
                    body.Add(new BodyMesh { Renderer = renderer, Skin = skin,
                        StaticVertices = skin == null ? points : null, Triangles = triangles });
                }
                Assert.That(body.Count, Is.GreaterThan(10), "Inspect the continuous modular hero and wardrobe.");
            }

            internal void AssertClear(string label)
            {
                foreach (BodyMesh part in body)
                {
                    Renderer renderer = part.Renderer;
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    vertices.Clear();
                    if (part.Skin != null)
                    {
                        part.Skin.BakeMesh(baked, false);
                        baked.GetVertices(vertices);
                        // BakeMesh(false) already retains FBX scale: restore its
                        // compensated rigid frame without applying the 100x root twice.
                        for (int vertex = 0; vertex < vertices.Count; vertex++)
                            vertices[vertex] = renderer.transform.position + renderer.transform.rotation * vertices[vertex];
                    }
                    else
                        foreach (Vector3 point in part.StaticVertices)
                            vertices.Add(renderer.transform.TransformPoint(point));
                    Assert.That(vertices.Count, Is.GreaterThan(0), renderer.name);
                    Bounds worldBounds = new Bounds(vertices[0], Vector3.zero);
                    foreach (Vector3 vertex in vertices) worldBounds.Encapsulate(vertex);
                    nearby.Clear();
                    foreach (BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid solid in furniture)
                        if (solid.Overlaps(worldBounds)) nearby.Add(solid);
                    int[] triangles = part.Triangles;
                    for (int index = 0; index < triangles.Length; index += 3)
                        foreach (BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid solid in nearby)
                            if (solid.IntersectsConvexInterior(vertices[triangles[index]],
                                vertices[triangles[index + 1]], vertices[triangles[index + 2]], .002f))
                            {
                                AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior,
                                    label.Replace(' ', '-') + "-clearance-failure");
                                Assert.Fail($"{label}: visible {renderer.name} intersects {solid.Name} beyond 2 mm, triangle {index / 3}: " +
                                    $"{vertices[triangles[index]]:F5}, {vertices[triangles[index + 1]]:F5}, {vertices[triangles[index + 2]]:F5}.");
                            }
                }
            }

            public void Dispose() => Object.DestroyImmediate(baked);
        }
    }
}
