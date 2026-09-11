using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Three continuous cartons: receiver carries, weighs outside shipping, approves and clears before loading; contacts, routes, restore and pause.")]
        public IEnumerator CityCanneryInspection() => CaptureFocusedPort(CaptureCanneryInspection);

        private static IEnumerator CaptureCanneryInspection(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityCanneryController cannery = city.Cannery;
            CityCanneryPlan plan = cannery.Plan;
            var failures = new List<Exception>();
            bool manualSpeech = cannery.FactoryConversation.UseManualClock;
            Vector3 hero = city.Player.GameObject.transform.position;
            double savedWork = cannery.WorkingSeconds, savedLife = cannery.LifeSeconds;
            try
            {
                GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                cannery.FactoryConversation.UseManualClock = true;
                cannery.FactoryConversation.Suspend();
                cannery.ApplyLifeAt(100d);
                city.Player.Motor.Teleport(plan.World(new Vector3(8f, .3f, -7f)));
                var cartons = new Transform[CityFishSupplyCycle.HandlingUnits];
                for (int unit = 0; unit < cartons.Length; unit++) cartons[unit] = cannery.FinishedBox(unit);
                DeferCanneryContract(failures, "scale is physically outside shipping", () =>
                    AssertCanneryShippingScale(cannery));
                DeferCanneryContract(failures, "receiving never loads the outgoing scale", () =>
                {
                    cannery.ApplyAt(TransferTime(cannery, CityFishSupplyStage.UnloadFish, 0, .86f));
                    Assert.That(cannery.ShippingScaleWeight, Is.Zero);
                    Assert.That(cannery.Snapshot.Inspection.IsActive, Is.False);
                });
                for (int batch = 0; batch < 2; batch++)
                {
                    for (int unit = 0; unit < cartons.Length; unit++)
                    {
                        int currentUnit = unit, currentBatch = batch;
                        DeferCanneryContract(failures, "inspection batch " + batch + " carton " + unit, () =>
                            AssertCanneryCartonInspection(cannery, cartons, currentUnit, currentBatch));
                        Debug.Log("CANNERY INSPECTION: sampled batch " + batch + ", carton " + unit);
                        yield return null;
                    }
                    int sampledBatch = batch;
                    DeferCanneryContract(failures, "loading waits for final approval and clearance, batch " + batch, () =>
                    {
                        double load = cannery.Cycle.StageStart(CityFishSupplyStage.LoadFinished, sampledBatch);
                        Assert.That(load, Is.EqualTo(cannery.Cycle.InspectionFinishedAt(sampledBatch)).Within(.000001d));
                        cannery.ApplyAt(load - .001d);
                        Assert.That(cannery.Snapshot.Stage, Is.EqualTo(CityFishSupplyStage.WaitForProduction));
                        Assert.That(cannery.Snapshot.Inspection.StoredUnits, Is.EqualTo(3));
                        Assert.That(cannery.Snapshot.TruckCases, Is.Zero);
                        cannery.ApplyAt(load + .001d);
                        Assert.That(cannery.Snapshot.Stage, Is.EqualTo(CityFishSupplyStage.LoadFinished));
                        Assert.That(cannery.Snapshot.Inspection.ApprovedUnits, Is.EqualTo(3));
                        Assert.That(cannery.Snapshot.Inspection.IsActive, Is.False);
                        Assert.That(cannery.ShippingScaleWeight, Is.Zero);
                        cannery.ApplyAt(cannery.Cycle.StageStart(CityFishSupplyStage.FactoryToShop, sampledBatch));
                        Assert.That(cannery.Snapshot.TruckCases, Is.EqualTo(3));
                        for (int unit = 0; unit < cartons.Length; unit++)
                        {
                            Assert.That(cannery.FinishedBox(unit), Is.SameAs(cartons[unit]));
                            Assert.That(cartons[unit].gameObject.activeInHierarchy, Is.True);
                        }
                    });
                }
                DeferCanneryContract(failures, "inspector and moving cartons clear site collision", () =>
                    AssertCanneryInspectionRoute(city, cannery));
                DeferCanneryContract(failures, "approved supports and loading clear the outdoor equipment and waiting crew", () =>
                    AssertCanneryInspectedLoading(cannery));
                DeferCanneryContract(failures, "cold reconstruction keeps carton custody and pose", () =>
                    AssertCanneryInspectionRestore(city, cannery, port));

                foreach (CityCanneryInspectionStage phase in new[] { CityCanneryInspectionStage.Pickup,
                    CityCanneryInspectionStage.CarryToScale, CityCanneryInspectionStage.Settle,
                    CityCanneryInspectionStage.Approve, CityCanneryInspectionStage.CarryToReady })
                {
                    double time = InspectionTime(cannery, phase, 0,
                        phase == CityCanneryInspectionStage.CarryToScale ? .88d : .5d);
                    bool inside = phase == CityCanneryInspectionStage.Pickup;
                    Vector3 from = inside ? plan.World(new Vector3(-1.2f, 2.05f, 5.4f)) :
                        plan.World(new Vector3(4.7f, 2.2f, 8.6f));
                    cannery.ApplyAt(time);
                    Vector3 target = inside ? cannery.FinishedBox(0).position + Vector3.up * .45f :
                        cannery.ShippingScaleLoadPosition + Vector3.up * .7f;
                    yield return CaptureCannery(camera, city, cannery, time,
                        "inspection-" + phase.ToString().ToLowerInvariant(), from, target);
                }
                double loadingFrame = TransferTime(cannery, CityFishSupplyStage.LoadFinished, 0, .19f);
                cannery.ApplyAt(loadingFrame);
                yield return CaptureCannery(camera, city, cannery, loadingFrame, "inspection-approved-carton-loading",
                    plan.World(new Vector3(4.7f, 2.2f, 5.1f)), cannery.FinishedBox(0).position + Vector3.up * .45f);
                double heldTime = InspectionTime(cannery, CityCanneryInspectionStage.CarryToScale, 0, .5d);
                cannery.AutoAdvance = true;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    cannery.ApplyAt(heldTime);
                    yield return null;
                    Vector3 box = cannery.FinishedBox(0).position;
                    Vector3 hand = cannery.GetFactoryWorker(0).RightGrip.position;
                    Quaternion head = cannery.GetFactoryWorker(0).Head.rotation;
                    double life = cannery.LifeSeconds;
                    yield return null;
                    yield return null;
                    DeferCanneryContract(failures, "pause holds carried box, hands, nod and clocks", () =>
                    {
                        Assert.That(cannery.WorkingSeconds, Is.EqualTo(heldTime));
                        Assert.That(cannery.LifeSeconds, Is.EqualTo(life));
                        Assert.That(cannery.FinishedBox(0).position, Is.EqualTo(box));
                        Assert.That(cannery.GetFactoryWorker(0).RightGrip.position, Is.EqualTo(hand));
                        Assert.That(cannery.GetFactoryWorker(0).Head.rotation, Is.EqualTo(head));
                    });
                }
            }
            finally
            {
                cannery.AutoAdvance = false;
                cannery.FactoryConversation.Suspend();
                cannery.FactoryConversation.UseManualClock = manualSpeech;
                cannery.ApplyAt(savedWork);
                cannery.ApplyLifeAt(savedLife);
                cannery.AdvanceSounds(false);
                city.Player.Motor.Teleport(hero);
                CityFishSupplySession.ResetForNewGame();
            }
            if (failures.Count > 0) throw new AggregateException("Cannery inspection contracts failed.", failures);
            Debug.Log("CITY CANNERY INSPECTION OK: outside scale, continuous cartons and hands, approval gate, paths, restoration and pause.");
        }

        private static double InspectionTime(CityCanneryController cannery, CityCanneryInspectionStage phase,
            int unit, double progress, long batch = 0) => cannery.Cycle.InspectionPhaseStart(phase, unit, batch) +
                cannery.Cycle.InspectionPhaseDuration(phase, unit) * progress;

        private static void AssertCanneryShippingScale(CityCanneryController cannery)
        {
            Transform scale = CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject, "ShippingScale");
            Assert.That(scale, Is.Not.Null, "The complete authored weighing equipment moves together.");
            bool any = false;
            foreach (MeshFilter mesh in scale.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 local = cannery.Plan.Local(mesh.transform.TransformPoint(vertex));
                    Assert.That(local.x, Is.GreaterThan(.12f), "Every part of the scales lies beyond the hall wall.");
                    Assert.That(local.z, Is.GreaterThan(6.1f), "The shipping door and its ramp remain clear.");
                    Assert.That(local.x, Is.LessThan(2f), "Scale stays in its facade pocket, away from the truck lane.");
                    any = true;
                }
            Assert.That(any, Is.True);
            Vector3 load = cannery.Plan.Local(cannery.ShippingScaleLoadPosition);
            Assert.That(load.x, Is.GreaterThan(.12f));
            Assert.That(load.z, Is.InRange(6.1f, 8f));
        }

        private static void AssertCanneryCartonInspection(CityCanneryController cannery, Transform[] cartons,
            int unit, long batch)
        {
            var actor = cannery.GetFactoryWorker(0);
            double packed = cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Pack, unit, batch) +
                cannery.Cycle.ProductionStageDuration(CityCanneryProductionStage.Pack);
            Assert.That(cannery.Cycle.InspectionStart(unit, batch), Is.GreaterThanOrEqualTo(packed));
            foreach (CityCanneryInspectionStage phase in Enum.GetValues(typeof(CityCanneryInspectionStage)))
            {
                if (phase == CityCanneryInspectionStage.Idle) continue;
                double start = cannery.Cycle.InspectionPhaseStart(phase, unit, batch);
                cannery.ApplyAt(start - .0001d);
                Vector3 before = cartons[unit].position, receiverBefore = actor.transform.position;
                cannery.ApplyAt(start + .0001d);
                Assert.That(Vector3.Distance(cartons[unit].position, before), Is.LessThan(.025f), phase + " carton boundary");
                Assert.That(Vector3.Distance(actor.transform.position, receiverBefore), Is.LessThan(.025f), phase + " receiver boundary");
                foreach (double progress in new[] { .15d, .5d, .85d })
                {
                    cannery.ApplyAt(InspectionTime(cannery, phase, unit, progress, batch));
                    Assert.That(cannery.Snapshot.Inspection.Stage, Is.EqualTo(phase));
                    Assert.That(cannery.Snapshot.Inspection.UnitIndex, Is.EqualTo(unit));
                    Assert.That(cannery.Snapshot.AccountedUnits, Is.EqualTo(3));
                    Assert.That(cannery.Snapshot.TruckCases, Is.Zero);
                    Assert.That(cannery.FinishedBox(unit), Is.SameAs(cartons[unit]));
                    Assert.That(cartons[unit].gameObject.activeInHierarchy, Is.True);
                    Assert.That(cannery.FactoryWorkerHandsFree(0), Is.False, "Inspection owns the receiver's gestures.");
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    if (cannery.InspectionBoxInHands)
                    {
                        Assert.That(Vector3.Distance(actor.RightGrip.position, cannery.InspectionRightHandTarget), Is.LessThan(.04f));
                        Assert.That(Vector3.Distance(actor.LeftGrip.position, cannery.InspectionLeftHandTarget), Is.LessThan(.04f));
                        Bounds body = InspectionRenderedBounds(cartons[unit]);
                        Assert.That(body.SqrDistance(actor.RightGrip.position), Is.LessThan(.07f * .07f));
                        Assert.That(body.SqrDistance(actor.LeftGrip.position), Is.LessThan(.07f * .07f));
                    }
                    if (phase == CityCanneryInspectionStage.Settle || phase == CityCanneryInspectionStage.Approve)
                    {
                        Assert.That(cannery.InspectionBoxInHands, Is.False, "The platform supports the released carton.");
                        Assert.That(Vector3.Distance(cartons[unit].position, cannery.ShippingScaleLoadPosition), Is.LessThan(.01f));
                        Assert.That(cannery.ShippingScaleWeight, Is.GreaterThan(.5f));
                        Assert.That(cannery.Snapshot.Inspection.ApprovedUnits, Is.EqualTo(unit));
                    }
                    if (phase == CityCanneryInspectionStage.CarryToReady || phase == CityCanneryInspectionStage.PutAway ||
                        phase == CityCanneryInspectionStage.Clear)
                        Assert.That(cannery.ShippingScaleWeight, Is.Zero, "A removed box leaves the scale unloaded.");
                }
            }
            cannery.ApplyAt(InspectionTime(cannery, CityCanneryInspectionStage.Approve, unit, .01d, batch));
            Quaternion headBefore = actor.Head.rotation;
            cannery.ApplyAt(InspectionTime(cannery, CityCanneryInspectionStage.Approve, unit, .5d, batch));
            Assert.That(Quaternion.Angle(headBefore, actor.Head.rotation), Is.GreaterThan(2f), "Visible approval uses the existing receiver's head.");
            Bounds carton = InspectionRenderedBounds(cartons[unit]);
            Assert.That(carton.size.y, Is.InRange(.25f, .45f), "One carton replaces the old fused stack.");
            int count = 0;
            foreach (Transform part in cannery.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("Finished handling unit ", StringComparison.Ordinal)) count++;
            Assert.That(count, Is.EqualTo(3), "Finite box identities include hidden objects.");
        }

        private static Bounds InspectionRenderedBounds(Transform root)
        {
            Bounds bounds = default;
            bool found = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                found = true;
            }
            Assert.That(found, Is.True, root.name + " has real rendered geometry.");
            return bounds;
        }

        private static void AssertCanneryInspectionRoute(CityGameRoot city, CityCanneryController cannery)
        {
            var solids = new HashSet<Collider>(cannery.Equipment.GetComponentsInChildren<Collider>(true));
            foreach (Transform root in city.World.DistrictPointOfInterestRoot.GetComponentsInChildren<Transform>(true))
                if (root.name == "Industrial Cannery" && root.Find("Hall") != null)
                    foreach (Collider solid in root.GetComponentsInChildren<Collider>(true)) solids.Add(solid);
            var overlaps = new Collider[32];
            var faults = new Dictionary<string, string>();
            var actor = cannery.GetFactoryWorker(0);
            for (int unit = 0; unit < 3; unit++)
            {
                double start = cannery.Cycle.InspectionStart(unit), end = start + cannery.Cycle.InspectionPlan.UnitDuration(unit);
                for (double time = start + .05d; time < end; time += .6d)
                {
                    cannery.ApplyAt(time);
                    Physics.SyncTransforms();
                    int count = Physics.OverlapCapsuleNonAlloc(actor.transform.position + Vector3.up * .35f,
                        actor.Head.position, .29f, overlaps, ~0, QueryTriggerInteraction.Ignore);
                    Assert.That(count, Is.LessThan(overlaps.Length));
                    for (int hit = 0; hit < count; hit++)
                    {
                        Collider solid = overlaps[hit];
                        if (!solids.Contains(solid) || solid.transform.IsChildOf(actor.transform)) continue;
                        if (!faults.ContainsKey(solid.name)) faults.Add(solid.name,
                            actor.name + " intersects " + solid.name + " at " + time.ToString("F2") + ", " +
                            cannery.Plan.Local(actor.transform.position).ToString("F3"));
                    }
                }
            }
            Assert.That(faults, Is.Empty, string.Join("\n", faults.Values));
        }

        private static void AssertCanneryInspectionRestore(CityGameRoot city, CityCanneryController cannery, CityPortController port)
        {
            double seek = InspectionTime(cannery, CityCanneryInspectionStage.CarryToScale, 1, .5d, 1);
            cannery.ApplyAt(seek);
            Vector3 box = cannery.FinishedBox(1).position, receiver = cannery.GetFactoryWorker(0).transform.position;
            Quaternion head = cannery.GetFactoryWorker(0).Head.rotation;
            int approved = cannery.Snapshot.Inspection.ApprovedUnits;
            cannery.ApplyAt(0d);
            cannery.ApplyAt(seek);
            Assert.That(cannery.FinishedBox(1).position, Is.EqualTo(box));
            Assert.That(cannery.GetFactoryWorker(0).Head.rotation, Is.EqualTo(head));
            var host = new GameObject("Cannery inspection cold reconstruction probe");
            host.SetActive(false);
            try
            {
                CityCanneryController other = CityCanneryController.Build(host.transform, city.Layout, port, city.Player.GameObject.transform);
                other.AutoAdvance = false;
                other.ForcePresentation = true;
                other.ApplyLifeAt(cannery.LifeSeconds);
                other.ApplyAt(seek);
                Assert.That(Vector3.Distance(other.FinishedBox(1).position, box), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(other.GetFactoryWorker(0).transform.position, receiver), Is.LessThan(.001f));
                Assert.That(other.Snapshot.Inspection.ApprovedUnits, Is.EqualTo(approved));
                Assert.That(other.Snapshot.AccountedUnits, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                port.AutoAdvance = false;
                port.IsSupplyDriven = true;
                cannery.ApplyAt(seek);
            }
        }

        private static void AssertCanneryInspectedLoading(CityCanneryController cannery)
        {
            var supports = new List<Transform>();
            foreach (Transform part in cannery.GetComponentsInChildren<Transform>(true))
                if (part.name == "ShippingPallet") supports.Add(part);
            Assert.That(supports.Count, Is.EqualTo(3), "Each approved carton has one independent support.");
            var driver = cannery.transform.Find("Fish Delivery Driver").GetComponent<VillageResidentPresentation>();
            var scaleSolids = new HashSet<Collider>();
            foreach (Collider collider in cannery.Equipment.GetComponentsInChildren<Collider>(true))
                if (collider.name.StartsWith("COL_ShippingScale", StringComparison.Ordinal)) scaleSolids.Add(collider);
            Assert.That(scaleSolids.Count, Is.EqualTo(2), "Platform and stand collisions accompany the moved visible scale.");
            var overlaps = new Collider[32];
            for (int unit = 0; unit < 3; unit++)
                foreach (float progress in new[] { .03f, .14f, .19f, .24f, .30f, .34f, .41f, .52f, .64f, .78f, .93f })
                {
                    cannery.ApplyAt(TransferTime(cannery, CityFishSupplyStage.LoadFinished, unit, progress));
                    Assert.That(cannery.Snapshot.Inspection.ApprovedUnits, Is.EqualTo(3));
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    Physics.SyncTransforms();
                    Bounds carton = InspectionRenderedBounds(cannery.FinishedBox(unit));
                    Transform support = null;
                    float nearest = float.PositiveInfinity;
                    foreach (Transform candidate in supports)
                    {
                        float distance = Vector3.Distance(candidate.position, cannery.FinishedBox(unit).position);
                        if (distance >= nearest) continue;
                        support = candidate; nearest = distance;
                    }
                    Assert.That(support, Is.Not.Null);
                    Bounds pallet = InspectionRenderedBounds(support);
                    Assert.That(carton.min.y, Is.EqualTo(pallet.max.y).Within(.025f), "The real carton bottom rests on its pallet top.");
                    Assert.That(cannery.Plan.Local(pallet.center).x, Is.GreaterThan(0f), "Loading uses the outdoor approved buffer.");
                    if (progress >= .35f) continue;
                    int count = Physics.OverlapCapsuleNonAlloc(driver.transform.position + Vector3.up * .35f,
                        driver.Head.position, .29f, overlaps, ~0, QueryTriggerInteraction.Ignore);
                    Assert.That(count, Is.LessThan(overlaps.Length));
                    for (int hit = 0; hit < count; hit++)
                        Assert.That(scaleSolids.Contains(overlaps[hit]), Is.False, "The loading operator clears the complete outdoor scale.");
                    for (int role = 0; role < 4; role++)
                    {
                        Vector3 difference = cannery.GetFactoryWorker(role).transform.position - driver.transform.position;
                        Assert.That(new Vector2(difference.x, difference.z).magnitude, Is.GreaterThan(.58f),
                            "Loading operator clears waiting worker " + role + " for carton " + unit + ", phase " + progress);
                    }
                }
        }
    }
}
