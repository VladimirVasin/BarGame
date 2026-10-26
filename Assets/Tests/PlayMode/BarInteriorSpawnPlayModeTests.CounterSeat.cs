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
                    string prefix = "seat-contact-" + (seatIndex + 1);
                    var support = new CounterSeatSupportTrace(plan);
                    clearance.SelectSeat(plan);
                    void StageObserver(bool rear = false)
                    {
                        Vector3 target = plan.InteractionPosition + Vector3.up * .08f;
                        Vector3 side = plan.EntryPose.RootRotation * Vector3.right * (int)plan.AccessSide;
                        Vector3 forward = plan.EntryPose.RootRotation * Vector3.forward;
                        Vector3 offset = rear ? -forward * 1.65f + side * .3f : side * 1.5f - forward * .8f;
                        camera.transform.SetPositionAndRotation(
                            target + offset + Vector3.up * .60f,
                            Quaternion.LookRotation(-offset - Vector3.up * .60f, Vector3.up));
                        camera.fieldOfView = 58f;
                    }
                    void CaptureProgress(string phase, int index)
                    {
                        StageObserver();
                        AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                            prefix + "-" + phase + "-" + index + "-side");
                        StageObserver(true);
                        AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.BarInterior,
                            prefix + "-" + phase + "-" + index + "-rear");
                        StageObserver();
                    }
                    StageObserver();
                    yield return null;

                    int frames = 0;
                    bool sawEnter = false, sawSide = false;
                    int nextShot = 0;
                    float[] shotProgress = { 0f, .18f, .40f, .48f, .58f, .68f, .84f, .98f };
                    while (interaction.Phase != PlayerAnimatedInteractionPhase.Looping && frames++ < 300)
                    {
                        interaction.AdvanceExternal(clock, .05f);
                        yield return AtFinalBarPose(finalPose, () =>
                        {
                            StageObserver();
                            CounterSeatSupportSample contacts = clearance.AssertClear(prefix + " enter " + interaction.PhaseProgress.ToString("F3"));
                            if (interaction.Phase != PlayerAnimatedInteractionPhase.Entering) return;
                            if (!clearance.HasFloorReference)
                            {
                                Assert.That(interaction.PhaseProgress, Is.LessThan(.03f),
                                    "Measure the boot clearance in the neutral handoff, before the side steps.");
                                contacts = clearance.UseStandingFloorReference(contacts);
                            }
                            sawEnter = true;
                            Vector3 pelvis = ((Player3DCharacterPresentation)bar.Player.Visual).Registry.Anchors.Pelvis.position;
                            if (Mathf.Abs(pelvis.x - plan.ActionHipPosition.x) > .38f) sawSide = true;
                            try { support.ObserveEntering(interaction.PhaseProgress, pelvis, contacts); }
                            catch (AssertionException) { CaptureProgress("enter-support-failure", nextShot); throw; }
                            if (nextShot < shotProgress.Length && interaction.PhaseProgress >= shotProgress[nextShot])
                            {
                                CaptureProgress("enter", nextShot);
                                nextShot++;
                            }
                        });
                    }
                    Assert.That(interaction.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
                    Assert.That(sawEnter && sawSide, Is.True,
                        "Entry must visibly pass beside the seat before the first supported hip contact.");
                    yield return AtFinalBarPose(finalPose, () => support.AssertSeated(clearance.AssertClear(prefix + " seated")));
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
                            CounterSeatSupportSample contacts = clearance.AssertClear(prefix + " exit " + interaction.PhaseProgress.ToString("F3"));
                            if (interaction.Phase != PlayerAnimatedInteractionPhase.Exiting) return;
                            sawExit = true;
                            Vector3 pelvis = ((Player3DCharacterPresentation)bar.Player.Visual).Registry.Anchors.Pelvis.position;
                            try { support.ObserveExiting(interaction.PhaseProgress, contacts); }
                            catch (AssertionException) { CaptureProgress("exit-support-failure", nextShot); throw; }
                            if (Mathf.Abs(pelvis.x - plan.ActionHipPosition.x) > .38f) sawExitSide = true;
                            if (nextShot < shotProgress.Length && interaction.PhaseProgress >= shotProgress[nextShot])
                            {
                                CaptureProgress("exit", nextShot);
                                nextShot++;
                            }
                        });
                    }
                    Assert.That(interaction.IsActive, Is.False);
                    Assert.That(sawExit && sawExitSide, Is.True, "Exit must use the same side passage.");
                    support.AssertFinished();
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

        private struct CounterSeatSupportSample
        {
            internal float HipGap;
            internal bool LeftHip, RightHip;
            internal float LeftSole, RightSole;
            internal Vector3 LeftFoot, RightFoot;
            internal float BraceGap;
            internal bool Hip => HipGap <= .025f;
            internal bool Floor => LeftSole <= .015f || RightSole <= .015f;
            public override string ToString() => $"hipGap={HipGap:F4}, hips={LeftHip}/{RightHip}, soles={LeftSole:F4}/{RightSole:F4}, brace={BraceGap:F4}";
        }

        private sealed class CounterSeatSupportTrace
        {
            private readonly CounterSeatPlan plan;
            private bool touchedHip, sawOneHip, sawTwoHips, sawFloorBeforeHip;
            private bool sawExitFloor, releasedHip;
            private int plantedFoot = -1;
            private Vector3 plantedAt;

            internal CounterSeatSupportTrace(CounterSeatPlan plan) => this.plan = plan;

            internal void ObserveEntering(float progress, Vector3 pelvis, CounterSeatSupportSample sample)
            {
                string label = $"entry {progress:F3}: {sample}";
                Assert.That(sample.Floor || sample.Hip, Is.True,
                    label + ": the body must retain a real floor or seat support; a folded body suspended beside the stool is invalid.");
                if (progress >= .45f && progress <= .85f)
                    Assert.That(sample.BraceGap, Is.LessThanOrEqualTo(.025f),
                        label + ": the supporting palm must touch the actual counter while perching and climbing.");
                if (!touchedHip)
                {
                    sawFloorBeforeHip |= sample.Floor;
                    if (progress >= CounterSeatPlan.SideArrivalProgress) AssertPlantedFoot(sample, label);
                    if (sample.Hip)
                    {
                        touchedHip = true;
                        Vector3 lateral = plan.EntryPose.RootRotation * Vector3.right;
                        Assert.That(Mathf.Abs(Vector3.Dot(pelvis - plan.InteractionPosition, lateral)),
                            Is.GreaterThan(.10f), label + ": first touch must perch one hip at the edge before centring the pelvis.");
                        Assert.That(sample.LeftHip ^ sample.RightHip, Is.True,
                            label + ": first contact must use one clothed hip, with the other still off the seat.");
                        Assert.That(sample.Floor, Is.True,
                            label + ": the supporting boot must remain on the floor at first hip contact.");
                        sawOneHip = true;
                    }
                }
                else Assert.That(sample.Hip, Is.True,
                    label + ": the hip must keep touching the real seat throughout the climb to its centre.");
                sawTwoHips |= touchedHip && sample.LeftHip && sample.RightHip;
            }

            private void AssertPlantedFoot(CounterSeatSupportSample sample, string label)
            {
                int foot = plan.AccessSide == CounterSeatAccessSide.Right ? 1 : 0;
                Vector3 position = foot == 0 ? sample.LeftFoot : sample.RightFoot;
                float height = foot == 0 ? sample.LeftSole : sample.RightSole;
                Assert.That(height, Is.LessThanOrEqualTo(.015f),
                    label + ": the boot opposite the first hip contact must keep its neutral floor clearance.");
                if (plantedFoot < 0) { plantedFoot = foot; plantedAt = position; }
                Assert.That(Vector3.ProjectOnPlane(position - plantedAt, Vector3.up).magnitude,
                    Is.LessThan(.045f), label + ": the loaded boot must stay planted while the hip reaches the seat.");
            }

            internal void AssertSeated(CounterSeatSupportSample sample)
            {
                Assert.That(touchedHip && sawOneHip && sawFloorBeforeHip, Is.True,
                    "Entry must transfer support from the floor to one hip before the completed seated pose.");
                sawTwoHips |= sample.LeftHip && sample.RightHip;
                Assert.That(sample.Hip && sawTwoHips, Is.True,
                    "The completed pose must rest both clothed hips on the actual stool top: " + sample);
            }

            internal void ObserveExiting(float progress, CounterSeatSupportSample sample)
            {
                string label = $"exit {progress:F3}: {sample}";
                Assert.That(sample.Floor || sample.Hip, Is.True,
                    label + ": standing must keep a real seat or floor support.");
                sawExitFloor |= sample.Floor;
                if (!sample.Hip)
                {
                    Assert.That(sawExitFloor, Is.True,
                        label + ": a boot must reach the floor before the hip leaves the seat.");
                    releasedHip = true;
                }
                else Assert.That(releasedHip, Is.False,
                    label + ": the body must not lose seat support then land on it again during standing.");
            }

            internal void AssertFinished() => Assert.That(sawExitFloor && releasedHip, Is.True,
                "The visible exit must transfer its weight from the seat back onto the floor.");
        }

        private sealed class CounterSeatSkinClearance : IDisposable
        {
            private sealed class BodyMesh
            {
                internal Renderer Renderer;
                internal SkinnedMeshRenderer Skin;
                internal Vector3[] StaticVertices;
                internal int[] Triangles;
                internal bool HipCloth;
                internal int Sole = -1;
                internal int Hand = -1;
            }

            private sealed class TopSurface
            {
                internal Vector3 A, B, C;
                internal Vector3 Centre;
            }

            private readonly List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid> furniture =
                new List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid>();
            private readonly List<BodyMesh> body = new List<BodyMesh>();
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid> nearby =
                new List<BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid>();
            private readonly Mesh baked = new Mesh();
            private readonly List<TopSurface> stoolTops = new List<TopSurface>();
            private readonly List<TopSurface> seatTop = new List<TopSurface>();
            private readonly List<TopSurface> counterTop = new List<TopSurface>();
            private readonly List<Vector3> clipA = new List<Vector3>(8), clipB = new List<Vector3>(8);
            private float floorTop;
            private Transform pelvis;
            private Vector3 hipRight;
            private int braceHand;
            private float floorReference;
            internal bool HasFloorReference { get; private set; }

            internal CounterSeatSkinClearance(BarInteriorRoot bar)
            {
                foreach (MeshFilter mesh in bar.Room.GetComponentsInChildren<MeshFilter>(true))
                {
                    string name = mesh.name;
                    if (name == "Floor") floorTop = mesh.GetComponent<Renderer>().bounds.max.y;
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
                    bool stool = name.StartsWith("Bar Stool ") || name.StartsWith("Bar Drink Service Stool");
                    if (stool || name == "Counter Top")
                    {
                        Bounds bounds = new Bounds(points[0], Vector3.zero);
                        foreach (Vector3 point in points) bounds.Encapsulate(point);
                        for (int index = 0; index < triangles.Length; index += 3)
                        {
                            Vector3 a = points[triangles[index]], b = points[triangles[index + 1]], c = points[triangles[index + 2]];
                            if (Mathf.Abs(a.y - bounds.max.y) > .001f || Mathf.Abs(b.y - bounds.max.y) > .001f ||
                                Mathf.Abs(c.y - bounds.max.y) > .001f || Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) < .98f) continue;
                            (stool ? stoolTops : counterTop).Add(new TopSurface { A = a, B = b, C = c, Centre = bounds.center });
                        }
                    }
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
                        StaticVertices = skin == null ? points : null, Triangles = triangles,
                        HipCloth = renderer.name == "CLO_TrousersPelvis" || renderer.name.StartsWith("CLO_TrousersThigh."),
                        Sole = renderer.name == "CLO_BootSole.L" ? 0 : renderer.name == "CLO_BootSole.R" ? 1 : -1,
                        Hand = renderer.name == "GEO_Hand.L" ? 0 : renderer.name == "GEO_Hand.R" ? 1 : -1 });
                }
                Assert.That(body.Count, Is.GreaterThan(10), "Inspect the continuous modular hero and wardrobe.");
                Assert.That(body.Exists(part => part.HipCloth), Is.True, "Measure the visible trousers at the hips.");
                Assert.That(body.FindAll(part => part.Sole >= 0), Has.Count.EqualTo(2), "Measure both authored boot soles.");
                pelvis = ((Player3DCharacterPresentation)bar.Player.Visual).Registry.Anchors.Pelvis;
            }

            internal void SelectSeat(CounterSeatPlan plan)
            {
                seatTop.Clear();
                foreach (TopSurface top in stoolTops)
                    if (Vector3.ProjectOnPlane(top.Centre - plan.InteractionPosition, Vector3.up).sqrMagnitude < .0025f &&
                        Mathf.Abs(top.A.y - plan.InteractionPosition.y) < .01f)
                        seatTop.Add(top);
                Assert.That(seatTop, Is.Not.Empty, "The support probe must use the selected real stool's top triangles.");
                hipRight = plan.EntryPose.RootRotation * Vector3.right;
                braceHand = plan.AccessSide == CounterSeatAccessSide.Right ? 0 : 1;
            }

            internal CounterSeatSupportSample UseStandingFloorReference(CounterSeatSupportSample sample)
            {
                // Preserve the authored upright boot-to-floor clearance; do not
                // confuse that mesh allowance with an extra lift during entry.
                floorReference = Mathf.Min(sample.LeftSole, sample.RightSole);
                Assert.That(floorReference, Is.InRange(-.002f, .06f),
                    "The neutral hero must already stand next to the real floor before measuring support.");
                HasFloorReference = true;
                sample.LeftSole -= floorReference; sample.RightSole -= floorReference;
                return sample;
            }

            internal CounterSeatSupportSample AssertClear(string label)
            {
                var sample = new CounterSeatSupportSample {
                    HipGap = float.PositiveInfinity, LeftSole = float.PositiveInfinity, RightSole = float.PositiveInfinity,
                    BraceGap = float.PositiveInfinity };
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
                    if (part.Sole >= 0)
                    {
                        Vector3 contact = Vector3.zero;
                        int count = 0;
                        foreach (Vector3 vertex in vertices)
                            if (vertex.y <= worldBounds.min.y + .005f) { contact += vertex; count++; }
                        contact /= count;
                        float gap = worldBounds.min.y - floorTop;
                        Assert.That(gap, Is.GreaterThanOrEqualTo(-.002f), label + ": a boot penetrated the actual floor.");
                        if (part.Sole == 0) { sample.LeftSole = gap - floorReference; sample.LeftFoot = contact; }
                        else { sample.RightSole = gap - floorReference; sample.RightFoot = contact; }
                    }
                    nearby.Clear();
                    foreach (BarDrinkServiceWorldBuilderPlayModeTests.BottleSolid solid in furniture)
                        if (solid.Overlaps(worldBounds)) nearby.Add(solid);
                    int[] triangles = part.Triangles;
                    for (int index = 0; index < triangles.Length; index += 3)
                    {
                        if (part.HipCloth)
                            foreach (TopSurface top in seatTop)
                                AccumulateSupportContact(vertices[triangles[index]], vertices[triangles[index + 1]],
                                    vertices[triangles[index + 2]], top, true, ref sample);
                        if (part.Hand == braceHand)
                            foreach (TopSurface top in counterTop)
                                AccumulateSupportContact(vertices[triangles[index]], vertices[triangles[index + 1]],
                                    vertices[triangles[index + 2]], top, false, ref sample);
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
                return sample;
            }

            private void AccumulateSupportContact(Vector3 a, Vector3 b, Vector3 c, TopSurface top,
                bool hip, ref CounterSeatSupportSample sample)
            {
                // Clip the entire real garment triangle into the actual top triangle's
                // vertical prism. Vertex-only probes miss contact in a broad cloth face.
                clipA.Clear(); clipA.Add(a); clipA.Add(b); clipA.Add(c);
                List<Vector3> input = clipA, output = clipB;
                Vector3 centre = (top.A + top.B + top.C) / 3f;
                for (int edge = 0; edge < 3; edge++)
                {
                    Vector3 start = edge == 0 ? top.A : edge == 1 ? top.B : top.C;
                    Vector3 end = edge == 0 ? top.B : edge == 1 ? top.C : top.A;
                    Vector3 normal = Vector3.Cross(end - start, Vector3.up).normalized;
                    if (Vector3.Dot(normal, centre - start) > 0f) normal = -normal;
                    output.Clear();
                    Vector3 previous = input[input.Count - 1];
                    float previousDistance = Vector3.Dot(normal, previous - start);
                    foreach (Vector3 current in input)
                    {
                        float distance = Vector3.Dot(normal, current - start);
                        if ((distance <= 0f) != (previousDistance <= 0f))
                            output.Add(Vector3.LerpUnclamped(previous, current,
                                previousDistance / (previousDistance - distance)));
                        if (distance <= 0f) output.Add(current);
                        previous = current; previousDistance = distance;
                    }
                    if (output.Count == 0) return;
                    List<Vector3> swap = input; input = output; output = swap;
                }
                foreach (Vector3 point in input)
                {
                    float gap = Mathf.Abs(point.y - top.A.y);
                    if (!hip) { sample.BraceGap = Mathf.Min(sample.BraceGap, gap); continue; }
                    if ((point - pelvis.position).sqrMagnitude > .25f * .25f) continue;
                    sample.HipGap = Mathf.Min(sample.HipGap, gap);
                    if (gap > .025f) continue;
                    float side = Vector3.Dot(point - pelvis.position, hipRight);
                    if (side < -.035f) sample.LeftHip = true;
                    if (side > .035f) sample.RightHip = true;
                }
            }

            public void Dispose() => Object.DestroyImmediate(baked);
        }
    }
}
