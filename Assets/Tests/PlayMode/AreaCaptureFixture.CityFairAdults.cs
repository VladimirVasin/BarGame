using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static IEnumerator VerifyFairAdults(CityFairWorld fair, Camera camera,
            List<string> captures, CityFairReport report)
        {
            CityFairAdults adults = fair.Adults;
            Assert.That(adults, Is.Not.Null);
            Assert.That(adults.ActorCount, Is.EqualTo(DefaultNpcPopulation.FairVisitorCount));
            Assert.That(fair.Plan.AdultPositions.Length, Is.EqualTo(adults.ActorCount));
            Assert.That(fair.Plan.AdultFacings.Length, Is.EqualTo(adults.ActorCount));
            var signatures = new HashSet<string>();
            var identities = new string[adults.ActorCount];
            VillageResidentPresentation[] originals = adults.Actors.ToArray();
            report.adults = adults.ActorCount;
            adults.ApplyAt(adults.ElapsedSeconds);
            Physics.SyncTransforms();
            for (int i = 0; i < adults.ActorCount; i++)
            {
                VillageResidentPresentation actor = adults.Actors[i];
                identities[i] = DefaultNpcPopulation.FairVisitorId(i);
                DefaultNpcPopulation.Assignment assignment = DefaultNpcPopulation.GetAssignment(identities[i]);
                DefaultNpcAppearance appearance = actor.GetComponent<DefaultNpcAppearance>();
                NpcWardrobe wardrobe = actor.GetComponent<NpcWardrobe>();
                Assert.That(appearance, Is.Not.Null);
                Assert.That(wardrobe, Is.Not.Null);
                wardrobe.ValidateBindings();
                Assert.That(appearance.AppearanceKey, Is.EqualTo(identities[i]));
                Assert.That(appearance.CurrentFaceId, Is.EqualTo(assignment.FaceId));
                Assert.That(appearance.CurrentHairColorId, Is.EqualTo(assignment.HairColorId));
                CollectionAssert.AreEquivalent(assignment.ItemIds, wardrobe.EquippedItemIds);
                Assert.That(wardrobe.EquippedItemIds.Any(id => id.StartsWith("apron.")), Is.False,
                    "Visitors wear the population's ordinary clothes, without work aprons.");
                Assert.That(signatures.Add(assignment.VisibleSignature), Is.True, "Fair visitors have distinct assigned appearances.");
                Assert.That(Vector3.Distance(actor.transform.position, fair.Plan.AdultPositions[i]), Is.LessThan(.002f));
                Assert.That(Vector3.Dot(actor.transform.forward, fair.Plan.AdultFacings[i].normalized), Is.GreaterThan(.999f));
                AssertFairSupport(fair, actor.transform.position, identities[i] + " feet", report);
                Bounds visible = FairAdultVisibleBounds(actor);
                Assert.That(visible.size.y, Is.InRange(1.4f, 2.2f), identities[i] + " actual adult mesh height");
                float groundError = Mathf.Abs(visible.min.y - fair.Plan.SampleGroundY(visible.center));
                report.maximum_adult_ground_error_metres = Mathf.Max(report.maximum_adult_ground_error_metres, groundError);
                Assert.That(groundError, Is.LessThan(.12f), identities[i] + " visible soles must meet the sloping ground");
                CapsuleCollider body = actor.GetComponent<CapsuleCollider>();
                Assert.That(body != null && body.enabled && !body.isTrigger, Is.True, "Adults must have an actual solid body.");
                Assert.That(body.radius, Is.EqualTo(CityFairAdults.BodyRadius).Within(.0001f));
                Rect footprint = CityFairPlanner.Footprint(actor.transform.position,
                    CityFairAdults.BodyRadius * 2f, CityFairAdults.BodyRadius * 2f);
                foreach (Rect path in fair.Plan.ClearPaths)
                    Assert.That(path.Overlaps(footprint), Is.False, identities[i] + " leaves the crossing clear");
                foreach (Rect obstacle in fair.Plan.Obstacles)
                    Assert.That(obstacle.Overlaps(footprint), Is.False, identities[i] + " leaves props clear");
                for (int child = 0; child < fair.Children.ActorCount; child++)
                {
                    Vector3[] route = fair.Children.Route(child);
                    for (int segment = 1; segment < route.Length; segment++)
                    {
                        Vector3 offset = Vector3.ProjectOnPlane(route[segment] - route[segment - 1], Vector3.up);
                        Vector3 delta = Vector3.ProjectOnPlane(actor.transform.position - route[segment - 1], Vector3.up);
                        float along = offset.sqrMagnitude > .00001f ? Mathf.Clamp01(Vector3.Dot(delta, offset) / offset.sqrMagnitude) : 0f;
                        float distance = (delta - offset * along).magnitude;
                        Assert.That(distance, Is.GreaterThan(CityFairAdults.BodyRadius + CityFairChildren.BodyRadius),
                            identities[i] + " leaves the child's whole route clear");
                    }
                }
                foreach (Collider other in Physics.OverlapBox(body.bounds.center, body.bounds.extents,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (other.transform.IsChildOf(actor.transform)) continue;
                    bool intersects = Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out float depth);
                    Assert.That(intersects && depth > .01f, Is.False, identities[i] + " intersects " + other.name);
                }
                Vector3 facing = fair.Plan.AdultFacings[i];
                Vector3 right = Vector3.Cross(Vector3.up, facing);
                yield return FairFrame(camera, captures, "16-adult-visitor-" + i,
                    actor.transform.position + right * 1.9f + facing * .6f + Vector3.up * 1.55f,
                    actor.transform.position + Vector3.up * 1.05f, 62f);
            }
            report.adult_routes_clear = true;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                float seconds = adults.ElapsedSeconds;
                Vector3[] roots = adults.Actors.Select(actor => actor.transform.position).ToArray();
                Vector3[] hands = adults.Actors.Select(actor => actor.RightGrip.position).ToArray();
                Quaternion[] heads = adults.Actors.Select(actor => actor.Head.rotation).ToArray();
                for (int frame = 0; frame < 4; frame++) yield return null;
                Assert.That(adults.ElapsedSeconds, Is.EqualTo(seconds));
                for (int i = 0; i < adults.ActorCount; i++)
                {
                    Assert.That(Vector3.Distance(roots[i], adults.Actors[i].transform.position), Is.LessThan(.00001f));
                    Assert.That(Vector3.Distance(hands[i], adults.Actors[i].RightGrip.position), Is.LessThan(.00001f));
                    Assert.That(Quaternion.Angle(heads[i], adults.Actors[i].Head.rotation), Is.LessThan(.001f));
                }
                report.adult_pause_verified = true;
            }
            adults.enabled = false;
            Assert.That(adults.Actors.All(actor => !actor.gameObject.activeInHierarchy), Is.True);
            Assert.That(adults.Actors.All(actor => !actor.IsInitialized), Is.True, "Disable releases every animation graph.");
            Assert.That(NpcFootstepSources.Prune().Any(root => originals.Any(actor => actor.transform == root)), Is.False);
            adults.enabled = true;
            yield return null;
            CollectionAssert.AreEqual(originals, adults.Actors, "Re-entry restores the same adults without duplication.");
            Assert.That(adults.GetComponentsInChildren<VillageResidentPresentation>(true).Length, Is.EqualTo(originals.Length));
            for (int i = 0; i < adults.ActorCount; i++)
            {
                VillageResidentPresentation actor = adults.Actors[i];
                Assert.That(actor.gameObject.activeInHierarchy && actor.IsInitialized, Is.True);
                Assert.That(actor.GetComponent<DefaultNpcAppearance>().AppearanceKey, Is.EqualTo(identities[i]));
                CollectionAssert.AreEquivalent(DefaultNpcPopulation.GetAssignment(identities[i]).ItemIds,
                    actor.GetComponent<NpcWardrobe>().EquippedItemIds);
                Assert.That(NpcFootstepSources.Prune().Count(root => root == actor.transform), Is.EqualTo(1));
            }
            report.adult_disable_reentry_verified = true;
            Debug.Log("FAIR ADULTS: stable appearances, actual size/ground/collision, clear child routes, pause and re-entry verified.");
        }

        private static Bounds FairAdultVisibleBounds(VillageResidentPresentation actor)
        {
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            var baked = new Mesh();
            try
            {
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        skin.BakeMesh(baked, true);
                        mesh = baked;
                    }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 world = renderer.transform.TransformPoint(vertex);
                        low = Vector3.Min(low, world); high = Vector3.Max(high, world);
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            Assert.That(float.IsInfinity(low.y), Is.False, "The adult must have visible geometry.");
            return new Bounds((low + high) * .5f, high - low);
        }
    }
}
