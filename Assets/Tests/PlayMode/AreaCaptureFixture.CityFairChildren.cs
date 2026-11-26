using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityFairChildCaptureAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            var models = Type.GetType("BarPromenade.Editor.CityFairChildAssetSetup, BarPromenade.Editor", true);
            models.GetMethod("ValidateOrThrow", Type.EmptyTypes).Invoke(null, null);
            var actions = Type.GetType("BarPromenade.Editor.CityFairChildActionAssetSetup, BarPromenade.Editor", true);
            actions.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("City fair child wardrobe: imported meshes, posed contacts and day/night frames.")]
        [Timeout(180000), PrebuildSetup(typeof(CityFairChildCaptureAssetsSetup))]
        public IEnumerator CityFairChildWardrobe()
        {
            float previousDelta = Time.captureDeltaTime;
            PlayerCameraFollow follow = null;
            bool previousFollow = false;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeAt(12 * 60);
                Time.captureDeltaTime = .05f;
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot city = null;
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = UnityEngine.Object.FindAnyObjectByType<CityGameRoot>();
                    if (city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True);
                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                follow = camera.GetComponent<PlayerCameraFollow>();
                if (follow != null) { previousFollow = follow.enabled; follow.enabled = false; }
                CityFairWorld fair = city.World.Fair;
                Light[] lights = AssertFairGarlandLights(city, fair);
                yield return VerifyFairChildren(city, fair, camera, new List<string>(), new CityFairReport(), lights);
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                if (follow != null) follow.enabled = previousFollow;
            }
        }

        private static Light[] AssertFairGarlandLights(CityGameRoot city, CityFairWorld fair)
        {
            CityNightAtmosphere atmosphere = city.Night.Atmosphere;
            Assert.That(fair.LampAnchors.Count, Is.EqualTo(5));
            var lights = new Light[fair.LampAnchors.Count];
            for (int i = 0; i < lights.Length; i++)
            {
                Transform anchor = fair.LampAnchors[i];
                int index = Enumerable.Range(0, atmosphere.LampAnchors.Count)
                    .Single(slot => atmosphere.LampAnchors[slot] == anchor);
                Light light = atmosphere.GetLightForAnchor(index);
                Assert.That(light != null && light.enabled && light.type == LightType.Spot, Is.True);
                Assert.That(light.intensity, Is.GreaterThanOrEqualTo(20f), "The fitted garland output must survive daylight dimming.");
                Assert.That(Vector3.Distance(light.transform.position, anchor.position), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(light.transform.rotation, anchor.rotation), Is.LessThan(.01f));
                lights[i] = light;
            }
            CityFairPlan plan = fair.Plan;
            var receivers = plan.Stalls.Select(stall => stall.Position + stall.Facing * 1.1f + Vector3.up * .95f)
                .Append((plan.CenterPathSouth + plan.CenterPathNorth) * .5f)
                .Append(plan.ChildTablePosition + Vector3.up * .55f)
                .Concat(plan.BenchPositions.Select(bench => bench + Vector3.up));
            foreach (Vector3 point in receivers)
                Assert.That(lights.Any(light =>
                {
                    Vector3 ray = point - light.transform.position;
                    return ray.sqrMagnitude < light.range * light.range * .81f &&
                        Vector3.Angle(light.transform.forward, ray) < light.spotAngle * .43f;
                }), Is.True, $"The garlands' direct cones must reach the fair at {point:F2}.");
            return lights;
        }

        private static IEnumerator FairChildFrame(Camera camera, List<string> captures, string name,
            Vector3 position, Vector3 target, float fov)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.fieldOfView = fov;
            yield return null;
            if (Application.isBatchMode)
                CaptureCurrentCamera(camera, "CityFair", name);
            else
            {
                // Use the completed player frame. A second synchronous camera
                // render can collide with URP's reflection probe updates.
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    Assert.That(frame != null && !IsBlank(frame), Is.True, name);
                    string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityFair");
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), frame.EncodeToPNG());
                }
                finally { if (frame != null) UnityEngine.Object.Destroy(frame); }
            }
            captures.Add(name + ".png");
        }

        private static IEnumerator VerifyFairChildren(CityGameRoot city, CityFairWorld fair, Camera camera,
            List<string> captures, CityFairReport report, Light[] garlandLights = null)
        {
            CityFairChildren children = fair.Children;
            Assert.That(children, Is.Not.Null);
            Assert.That(children.ActorCount, Is.EqualTo(3));
            report.children = children.ActorCount;
            report.child_visible_triangles = new int[3];
            var outfitIds = new HashSet<string>();
            var hairMeshes = new HashSet<Mesh>();
            var faceProperties = new MaterialPropertyBlock();
            Mesh commonHead = null;
            for (int i = 0; i < 3; i++)
            {
                CityFairChildPresentation actor = children.Actors[i];
                NpcWardrobe wardrobe = actor.GetComponent<NpcWardrobe>();
                Assert.That(wardrobe, Is.Not.Null);
                wardrobe.ValidateBindings();
                Assert.That(outfitIds.Add(wardrobe.CurrentOutfitId), Is.True, "All three outfits must be worn simultaneously.");
                var head = CityFairChildAssetProvider.FindPart(actor.gameObject, CityFairChildAssetProvider.FaceRendererName).GetComponent<Renderer>();
                actor.Sample(CityFairChildAction.Idle, 2.57f);
                head.GetPropertyBlock(faceProperties);
                Assert.That(faceProperties.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(.25f, .25f, .5f, .75f - i * .25f)),
                    "Blinking selects the closed-eye cell in this child's own row.");
                actor.ResetPose();
                var hair = CityFairChildAssetProvider.FindPart(actor.gameObject,
                    "OUTFIT_" + CityFairChildAssetProvider.OutfitNames[i] + "_Hair").GetComponent<SkinnedMeshRenderer>();
                Assert.That(hair.enabled && hairMeshes.Add(hair.sharedMesh), Is.True, "Each child wears its own hair shape.");
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
                Assert.That(triangles, Is.InRange(1, 12000), wardrobe.CurrentOutfitId + " actually visible triangles");
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
                            yield return FairChildFrame(camera, captures, "10-child-activity-" + i,
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
                        yield return FairChildFrame(camera, captures, "10-child-activity-2", focus + new Vector3(1.4f, 1.2f, 1.6f), focus + new Vector3(0f, .65f, -.3f), 52f);
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
                yield return CaptureFairChildQuality(city, fair, camera, captures, garlandLights);
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

        private static IEnumerator CaptureFairChildQuality(CityGameRoot city, CityFairWorld fair, Camera camera, List<string> captures,
            Light[] garlandLights)
        {
            CityFairChildren children = fair.Children;
            Vector3[] positions = children.Actors.Select(actor => actor.transform.position).ToArray();
            Quaternion[] rotations = children.Actors.Select(actor => actor.transform.rotation).ToArray();
            Vector3 origin = fair.Plan.CenterPathSouth;
            Transform adult = garlandLights != null ? fair.Adults.Actors[0].transform : null;
            Vector3 adultPosition = adult != null ? adult.position : default;
            Quaternion adultRotation = adult != null ? adult.rotation : default;
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
                yield return FairChildFrame(camera, captures, "11-child-hero-front", origin + new Vector3(.4f, 1.15f, 4.6f), origin + new Vector3(.4f, .85f, 0f), 52f);
                yield return FairChildFrame(camera, captures, "12-child-hero-back", origin + new Vector3(.4f, 1.15f, -4.6f), origin + new Vector3(.4f, .85f, 0f), 52f);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 point = children.Actors[i].transform.position;
                    yield return FairChildFrame(camera, captures, "13-child-side-" + i, point + new Vector3(.95f, .95f, .12f), point + Vector3.up * .75f, 68f);
                    yield return FairChildFrame(camera, captures, "14-child-face-" + i, point + new Vector3(.10f, 1.23f, .68f), point + Vector3.up * 1.17f, 42f);
                    Vector3 hand = children.Actors[i].RightGrip.position;
                    yield return FairChildFrame(camera, captures, "15-child-hand-" + i, hand + new Vector3(.22f, .05f, .34f), hand, 45f);
                }
                if (adult != null)
                {
                    Vector3 point = origin + Vector3.left * 1.75f;
                    point.y = fair.Plan.SampleGroundY(point);
                    adult.SetPositionAndRotation(point, Quaternion.identity);
                    Vector3 focus = origin + new Vector3(-.4f, .85f, 0f);
                    Vector3 view = origin + new Vector3(.1f, 1.36f, 4.2f);
                    yield return FairChildFrame(camera, captures, "16-child-npc-day", view, focus, 47f);
                    GameSessionState.AdvanceGameTime((float)((21 * 60 - GameSessionState.GameTimeOfDayMinutes) /
                        GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true);
                    AssertFairGarlandLights(city, fair);
                    yield return FairChildFrame(camera, captures, "17-child-npc-night", view, focus, 47f);
                    try
                    {
                        foreach (Light light in garlandLights) light.gameObject.SetActive(false);
                        yield return FairChildFrame(camera, captures, "18-child-npc-night-emission-only", view, focus, 47f);
                    }
                    finally
                    {
                        foreach (Light light in garlandLights) light.gameObject.SetActive(true);
                    }
                }
            }
            finally
            {
                if (adult != null) adult.SetPositionAndRotation(adultPosition, adultRotation);
                for (int i = 0; i < 3; i++) children.Actors[i].transform.SetPositionAndRotation(positions[i], rotations[i]);
                city.Player.Motor.Teleport(fair.Plan.CenterPathSouth + Vector3.up * PlayerFactory.GroundedRootOffset);
            }
        }
    }
}
