using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static IEnumerator VerifyFairChildren(CityGameRoot city, CityFairWorld fair, Camera camera,
            List<string> captures, CityFairReport report)
        {
            CityFairChildren children = fair.Children;
            Assert.That(children, Is.Not.Null);
            Assert.That(children.ActorCount, Is.EqualTo(3));
            report.children = children.ActorCount;
            report.child_visible_triangles = new int[3];
            var outfitIds = new HashSet<string>();
            Mesh commonHead = null;
            for (int i = 0; i < 3; i++)
            {
                CityFairChildPresentation actor = children.Actors[i];
                NpcWardrobe wardrobe = actor.GetComponent<NpcWardrobe>();
                Assert.That(wardrobe, Is.Not.Null);
                wardrobe.ValidateBindings();
                Assert.That(outfitIds.Add(wardrobe.CurrentOutfitId), Is.True, "All three outfits must be worn simultaneously.");
                int triangles = 0;
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh != null) triangles += mesh.triangles.Length / 3;
                    if (renderer.name == "GEO_Head")
                    {
                        if (commonHead == null) commonHead = mesh;
                        Assert.That(mesh, Is.SameAs(commonHead), "Outfits must share the authored child, not separately rebuilt bodies.");
                    }
                }
                Assert.That(triangles, Is.InRange(9000, 12000), wardrobe.CurrentOutfitId + " actually visible triangles");
                report.child_visible_triangles[i] = triangles;
                Assert.That(Vector3.Distance(actor.transform.lossyScale, Vector3.one), Is.LessThan(.00001f), "Child dimensions belong to its own model.");
                Assert.That(children.Route(i).All(fair.Plan.Contains), Is.True);
            }
            Assert.That(commonHead, Is.Not.Null);
            foreach (float x in new[] { -.33f, .33f })
                foreach (float z in new[] { -.205f, .205f })
                    AssertFairSupport(fair, children.Table.TransformPoint(new Vector3(x, 0f, z)), "Child table", report);

            CityBenchSitInteraction bench = city.GetComponentsInChildren<CityBenchSitInteraction>()
                .Single(item => item.Plan.Id == children.BenchId);
            children.enabled = false;
            Assert.That(CityBenchSeatClaims.IsClaimed(children.BenchId), Is.False);
            object playerSeatOwner = new object();
            Assert.That(CityBenchSeatClaims.TryClaim(children.BenchId, playerSeatOwner), Is.True);
            children.enabled = true;
            Vector3 waiting = children.Actors[2].transform.position;
            var photographed = new bool[3];
            try
            {
                city.Player.Motor.Teleport(fair.Plan.CenterPathSouth + Vector3.up * PlayerFactory.GroundedRootOffset);
                for (int frame = 0; frame < 290; frame++)
                {
                    yield return null;
                    Assert.That(Vector3.Distance(children.Actors[2].transform.position, waiting), Is.LessThan(.001f),
                        "A child must wait while the shared seat belongs to somebody else.");
                    CheckFairChildFrame(fair, report);
                    for (int i = 0; i < 2; i++)
                        if (!photographed[i] && children.Phase(i) == CityFairChildPhase.Looping)
                        {
                            Vector3 focus = children.Actors[i].transform.position;
                            yield return FairFrame(camera, captures, "10-child-activity-" + i,
                                focus + (i == 0 ? new Vector3(.85f, 1.35f, -1.6f) : new Vector3(1.0f, 1.1f, .90f)),
                                focus + Vector3.up * .72f, 54f);
                            photographed[i] = true;
                        }
                }
            }
            finally { CityBenchSeatClaims.Release(children.BenchId, playerSeatOwner); }

            bool checkedPlayerOffer = false;
            for (int frame = 0; frame < 1000 && (children.CompletedCycles(2) == 0 || !photographed.All(value => value)); frame++)
            {
                yield return null;
                CheckFairChildFrame(fair, report);
                if (children.OwnsBench && !checkedPlayerOffer)
                {
                    city.Player.Motor.Teleport(bench.Plan.EntryRootPosition);
                    Assert.That(bench.CanInteract(city.Player.Interactor), Is.False, "The actual player offer respects the child's shared claim.");
                    city.Player.Motor.Teleport(fair.Plan.CenterPathSouth + Vector3.up * PlayerFactory.GroundedRootOffset);
                    checkedPlayerOffer = true;
                }
                if (children.Phase(2) == CityFairChildPhase.Looping)
                {
                    Assert.That(Vector3.Distance(children.Actors[2].Pelvis.position,
                        fair.Plan.Benches[1].SeatTopCenter + Vector3.up * .07f), Is.LessThan(.035f), "Pelvis follows the actual sloping seat.");
                    if (!photographed[2])
                    {
                        Vector3 focus = children.Actors[2].transform.position;
                        yield return FairFrame(camera, captures, "10-child-activity-2", focus + new Vector3(1.4f, 1.2f, 1.6f), focus + new Vector3(0f, .65f, -.3f), 52f);
                        photographed[2] = true;
                    }
                }
            }
            Assert.That(photographed.All(value => value), Is.True, "All three real tasks must be reached.");
            Assert.That(checkedPlayerOffer, Is.True);
            Assert.That(children.CompletedCycles(0), Is.GreaterThan(0), "Toy stall enter/loop/exit");
            Assert.That(children.CompletedCycles(1), Is.GreaterThan(0), "Own car enter/loop/exit");
            Assert.That(children.CompletedCycles(2), Is.GreaterThan(0), "Bench enter/loop/exit");
            report.bench_claims_verified = true;
            report.child_completed_cycles = Enumerable.Range(0, 3).Select(children.CompletedCycles).ToArray();

            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                Vector3[] hips = children.Actors.Select(actor => actor.Pelvis.position).ToArray();
                Quaternion[] hands = children.Actors.Select(actor => actor.RightGrip.rotation).ToArray();
                Vector3 car = children.Car.position;
                float seconds = children.ElapsedSeconds;
                for (int frame = 0; frame < 4; frame++) yield return null;
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(Vector3.Distance(hips[i], children.Actors[i].Pelvis.position), Is.LessThan(.00001f));
                    Assert.That(Quaternion.Angle(hands[i], children.Actors[i].RightGrip.rotation), Is.LessThan(.001f));
                }
                Assert.That(Vector3.Distance(car, children.Car.position), Is.LessThan(.00001f));
                Assert.That(children.ElapsedSeconds, Is.EqualTo(seconds));
                report.child_pause_verified = true;
                yield return CaptureFairChildQuality(city, fair, camera, captures);
            }
            children.enabled = false;
            Assert.That(CityBenchSeatClaims.IsClaimed(children.BenchId), Is.False);
            Assert.That(children.Actors.All(actor => !actor.gameObject.activeInHierarchy), Is.True);
            children.enabled = true;
            yield return null;
            Assert.That(children.ActorCount, Is.EqualTo(3));
            Assert.That(children.Actors.All(actor => actor.gameObject.activeInHierarchy), Is.True);
            foreach (CityFairChildPresentation actor in children.Actors)
                Assert.That(NpcFootstepSources.Prune().Count(root => root == actor.transform), Is.EqualTo(1), "Re-enable must register each child's footsteps exactly once.");
            report.child_disable_reentry_verified = true;
            Debug.Log("FAIR CHILDREN: three visible wardrobes, grounded routes, actual hand contacts, shared bench, pause and re-entry verified.");
        }

        private static void CheckFairChildFrame(CityFairWorld fair, CityFairReport report)
        {
            CityFairChildren children = fair.Children;
            for (int i = 0; i < 3; i++)
            {
                CityFairChildPresentation actor = children.Actors[i];
                float groundError = Mathf.Abs(actor.transform.position.y - fair.Plan.SampleGroundY(actor.transform.position));
                report.maximum_child_root_ground_error_metres = Mathf.Max(report.maximum_child_root_ground_error_metres, groundError);
                Assert.That(groundError, Is.LessThan(.002f));
                Rect footprint = new Rect(actor.transform.position.x - CityFairChildren.BodyRadius,
                    actor.transform.position.z - CityFairChildren.BodyRadius, CityFairChildren.BodyRadius * 2f, CityFairChildren.BodyRadius * 2f);
                foreach (Rect path in fair.Plan.ClearPaths) Assert.That(path.Overlaps(footprint), Is.False, "Children leave every crossing clear.");
                bool sitting = i == 2 && (children.Phase(i) == CityFairChildPhase.Entering ||
                    children.Phase(i) == CityFairChildPhase.Looping || children.Phase(i) == CityFairChildPhase.Exiting);
                if (!sitting)
                    foreach (Transform foot in new[] { actor.LeftFoot, actor.RightFoot })
                    {
                        float soleClearance = foot.position.y - .065f - fair.Plan.SampleGroundY(foot.position);
                        Assert.That(soleClearance, Is.InRange(-.025f, .16f), $"Child {i} {actor.CurrentAction} {foot.name} sole clearance {soleClearance:F4}.");
                    }
                if (children.Phase(i) != CityFairChildPhase.Looping || i == 2) continue;
                foreach (var contact in new[] { (children.RightTarget(i), actor.RightGrip), (children.LeftTarget(i), actor.LeftGrip) })
                {
                    Assert.That(contact.Item1.HasValue, Is.True);
                    float error = Vector3.Distance(contact.Item1.Value, contact.Item2.position);
                    report.maximum_child_hand_error_metres = Mathf.Max(report.maximum_child_hand_error_metres, error);
                    report.child_contact_samples++;
                    Assert.That(error, Is.LessThan(.025f), $"Child {i} {actor.CurrentAction}: hand {contact.Item2.name}, target {contact.Item1.Value:F4}, actual {contact.Item2.position:F4}.");
                }
            }
        }

        private static IEnumerator CaptureFairChildQuality(CityGameRoot city, CityFairWorld fair, Camera camera, List<string> captures)
        {
            CityFairChildren children = fair.Children;
            Vector3[] positions = children.Actors.Select(actor => actor.transform.position).ToArray();
            Quaternion[] rotations = children.Actors.Select(actor => actor.transform.rotation).ToArray();
            Vector3 origin = fair.Plan.CenterPathSouth;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var point = origin + Vector3.right * ((i - 1) * .9f);
                    point.y = fair.Plan.SampleGroundY(point);
                    children.Actors[i].transform.SetPositionAndRotation(point, Quaternion.identity);
                    children.Actors[i].Sample(CityFairChildAction.Idle, 0f);
                    children.Actors[i].ApplyFootContacts(fair.Plan.SampleGroundY(children.Actors[i].LeftFoot.position),
                        fair.Plan.SampleGroundY(children.Actors[i].RightFoot.position));
                }
                Vector3 heroPoint = origin + Vector3.right * 1.85f;
                heroPoint.y = fair.Plan.SampleGroundY(heroPoint) + PlayerFactory.GroundedRootOffset;
                city.Player.Motor.Teleport(heroPoint);
                city.Player.GameObject.transform.rotation = Quaternion.identity;
                yield return FairFrame(camera, captures, "11-child-hero-front", origin + new Vector3(.4f, 1.15f, 4.6f), origin + new Vector3(.4f, .85f, 0f), 52f);
                yield return FairFrame(camera, captures, "12-child-hero-back", origin + new Vector3(.4f, 1.15f, -4.6f), origin + new Vector3(.4f, .85f, 0f), 52f);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 point = children.Actors[i].transform.position;
                    yield return FairFrame(camera, captures, "13-child-side-" + i, point + new Vector3(.95f, .95f, .12f), point + Vector3.up * .75f, 68f);
                    yield return FairFrame(camera, captures, "14-child-face-" + i, point + new Vector3(.10f, 1.23f, .68f), point + Vector3.up * 1.17f, 42f);
                    Vector3 hand = children.Actors[i].RightGrip.position;
                    yield return FairFrame(camera, captures, "15-child-hand-" + i, hand + new Vector3(.22f, .05f, .34f), hand, 45f);
                }
            }
            finally
            {
                for (int i = 0; i < 3; i++) children.Actors[i].transform.SetPositionAndRotation(positions[i], rotations[i]);
                city.Player.Motor.Teleport(fair.Plan.CenterPathSouth + Vector3.up * PlayerFactory.GroundedRootOffset);
            }
        }
    }
}
