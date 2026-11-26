using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        public IEnumerator JanitorRecoversLeafOnSlopingPavement()
        {
            var stage = new GameObject("Janitor on a sloped courtyard");
            Vector3 feet = new Vector3(0f, 1000f, 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(stage.transform);
            Quaternion slope = Quaternion.Euler(0f, 0f, 6f);
            Vector3 normal = slope * Vector3.up;
            floor.transform.SetPositionAndRotation(feet - normal * .1f, slope);
            floor.transform.localScale = new Vector3(5f, .2f, 5f);
            RuntimePrimitiveFactory.SetColor(floor.GetComponent<MeshRenderer>(), new Color32(101, 98, 88, 255));
            var camera = new GameObject("Courtyard camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform);
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.19f, .21f, .19f);
            camera.fieldOfView = 50f;
            Vector3 eye = feet + new Vector3(1.28f, 1.5f, 2.15f);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(feet + Vector3.up * .74f - eye));
            var light = new GameObject("Yard light").AddComponent<Light>();
            light.transform.SetParent(stage.transform);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(54f, 154f, 0f);
            bool fog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                var model = Resources.Load<GameObject>("City/PigeonLoft/PigeonLoft");
                var broom = CityPedestrianHandProps.FindSocket(model.transform, "Broom");
                var worker = new CityJanitorWorker(stage.transform, DefaultNpcPopulation.CityJanitorFirst, broom);
                worker.Arrive(feet, normal, Vector3.forward, 0f, 1f, 60f);
                worker.Body.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var loose = worker.Leaves.LooseLeaf;
                var bounds = loose.GetComponentInChildren<MeshRenderer>().bounds;
                Assert.That(bounds.size.magnitude, Is.InRange(.13f, .24f), "Use the imported leaf in metres.");
                worker.PoseAt(3f, 0f);
                Vector3 gathered = loose.position;
                worker.PoseAt(3.335f, 0f);
                Assert.That(Vector3.Dot(loose.position - feet, normal), Is.GreaterThan(.035f));
                worker.PoseAt(3.70f, 0f);
                Vector3 landing = loose.position;
                Assert.That(Vector3.Distance(gathered, landing), Is.GreaterThan(.22f));
                Assert.That(Mathf.Abs(Vector3.Dot(landing - feet, normal)), Is.LessThan(.015f));
                Assert.That(Vector3.Distance(worker.Bristles.position, landing), Is.GreaterThan(.20f));
                Quaternion watching = worker.Body.Head.rotation;
                yield return null;
                CaptureCurrentCamera(camera, "CityJanitorLeaf", "watched-leaf", 960, 640);
                worker.PoseAt(4.62f, 0f);
                Assert.That(Vector3.Distance(worker.Bristles.position, loose.position), Is.LessThan(.025f),
                    "The second sweep reaches the same settled leaf.");
                var hands = worker.Body.GetComponent<NpcHandPose>();
                Vector3 axis = (worker.Broom.position - worker.Bristles.position).normalized;
                foreach (bool left in new[] { false, true })
                    Assert.That(Vector3.ProjectOnPlane(hands.AnatomicalCylinderCentre(left) - worker.Broom.position, axis).magnitude,
                        Is.LessThan(.014f), "The palm must stay on the broom during the extra reach.");
                yield return null;
                CaptureCurrentCamera(camera, "CityJanitorLeaf", "second-sweep", 960, 640);
                worker.PoseAt(5.40f, 0f);
                Assert.That(Vector3.Distance(gathered, loose.position), Is.LessThan(.025f));
                worker.PoseAt(14f, 0f);
                Assert.That(Quaternion.Angle(watching, worker.Body.Head.rotation), Is.GreaterThan(6f));
                Assert.That(Vector3.Distance(worker.Feet, feet), Is.LessThan(.002f));
                Assert.That(Vector3.Distance(worker.Bristles.position, loose.position), Is.GreaterThan(.2f));
                worker.Leave();
                Assert.That(loose.gameObject.activeInHierarchy, Is.False);
            }
            finally
            {
                RenderSettings.fog = fog;
                Object.Destroy(stage);
            }
        }

        [UnityTest, Timeout(480000), Explicit("Janitor arrivals, work contacts and departures around a moving hero.")]
        public IEnumerator CityJanitorsFollowTheNeighborhood() => CaptureCityLocalAcceptance(ReviewJanitors);

        private static IEnumerator ReviewJanitors(CityGameRoot city)
        {
            var director = city.GetComponentInChildren<CityJanitorDirector>();
            Assert.That(director, Is.Not.Null);
            var courts = CityResidentialCourtyardPlan.ForLayout(city.Layout).Courts;
            Vector3 first = courts[0].CommonCenter;
            Vector3 second = courts.OrderByDescending(c => (first - c.CommonCenter).sqrMagnitude).First().CommonCenter;
            var sites = new CityJanitorSite(city);
            var fingerErrors = new List<string>();
            CityJanitorWorker previous = null;
            foreach (Vector3 basePoint in new[] { first, second })
            {
                city.Player.Motor.Teleport(basePoint);
                var view = city.Camera.transform;
                view.SetPositionAndRotation(basePoint + Vector3.up * 1.6f, Quaternion.LookRotation(Vector3.forward));
                for (int frame = 0; frame < 240 && !city.LowerDistrictReady; frame++) yield return null;
                Assert.That(city.LowerDistrictReady, Is.True);
                Physics.SyncTransforms();
                if (previous != null)
                {
                    Vector3 oldSite = previous.Feet;
                    view.position = oldSite + Vector3.up + Vector3.forward * 5f;
                    view.LookAt(oldSite + Vector3.up);
                    director.Advance(1f);
                    Assert.That(previous.Active && previous.Feet == oldSite, Is.True, "Keep the visible worker on site.");
                    view.position = basePoint + Vector3.up * 1.6f;
                    view.rotation = Quaternion.LookRotation(view.position - oldSite);
                    director.Advance(1f);
                    Assert.That(previous.Active && previous.Feet == oldSite, Is.False,
                        $"Previous site {oldSite} still occupied after moving the hero to {city.Player.GameObject.transform.position}.");
                }

                CityJanitorWorker worker = null;
                for (int attempt = 0; attempt < 30 && worker == null; attempt++)
                {
                    director.Advance(4f);
                    worker = director.Workers.FirstOrDefault(w => w != null && w != previous && w.Active &&
                        Vector3.Distance(w.Feet, basePoint) <= CityJanitorDirector.EntryFar + .5f);
                }
                Assert.That(worker, Is.Not.Null, $"No off-camera arrival around {basePoint}. Hero at " +
                    $"{city.Player.GameObject.transform.position}, workers at " +
                    string.Join(", ", director.Workers.Where(w => w?.Active == true).Select(w => w.Feet)));
                Assert.That(Vector3.Distance(worker.Feet, basePoint), Is.InRange(CityJanitorDirector.EntryNear - .5f,
                    CityJanitorDirector.EntryFar + .5f));
                Assert.That(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(city.Camera),
                    new Bounds(worker.Feet + Vector3.up, new Vector3(2f, 2.5f, 2f))), Is.False);
                Assert.That(sites.TryFit(worker.Feet, out _, out _), Is.True);
                Assert.That(worker.Body.GetComponentsInChildren<Collider>(true), Is.Empty);
                var wardrobe = worker.Body.GetComponent<NpcWardrobe>();
                Assert.That(wardrobe.EquippedItemIds, Does.Contain("outerwear.work"));
                var jacket = wardrobe.Garments.First(p => p.Renderer.name == "CLO_Outer_workBody").Renderer;
                var block = new MaterialPropertyBlock();
                jacket.GetPropertyBlock(block);
                Assert.That((Color32)block.GetColor("_BaseColor"), Is.EqualTo(new Color32(64, 101, 103, 255)));
                var hands = worker.Body.GetComponent<NpcHandPose>();
                var eye = FindLocalDetailEye(city, worker.Feet, worker.Body.transform.forward + worker.Body.transform.right * .55f, 3.2f);
                view.SetPositionAndRotation(eye, Quaternion.LookRotation(worker.Feet + Vector3.up * .90f - eye));
                city.Camera.fieldOfView = 47f;
                city.Visibility.Refresh(true);
                var firstStroke = Vector3.zero;
                foreach (float t in new[] { 2f, 2.5f, 3f, 3.5f, 8f, 14f, 17f })
                {
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        // Let Unity skin the same held pose that is submitted to the camera.
                        for (int frame = 0; frame < 2; frame++)
                        {
                            worker.PoseAt(t, t == 17f ? 1f : 0f);
                            yield return null;
                        }
                    }
                    Vector3 axis = (worker.Broom.position - worker.Bristles.position).normalized;
                    Vector3 upper = hands.AnatomicalCylinderCentre(false) - worker.Broom.position;
                    Vector3 lower = hands.AnatomicalCylinderCentre(true) - worker.Broom.position;
                    Assert.That(hands.LeftGripWeight, Is.EqualTo(1f));
                    Assert.That(hands.RightGripWeight, Is.EqualTo(1f));
                    foreach (var binding in hands.Hands)
                        Assert.That(Vector3.Angle(hands.CylinderAxis(binding.IsLeft),
                            axis), Is.LessThan(2f), "Wrist lost shaft alignment at " + t);
                    Assert.That(Vector3.ProjectOnPlane(upper, axis).magnitude, Is.LessThan(.012f), "Upper hand off shaft at " + t);
                    Assert.That(Vector3.ProjectOnPlane(lower, axis).magnitude, Is.LessThan(.012f), "Lower hand off shaft at " + t);
                    Assert.That(Vector3.Dot(upper, axis), Is.InRange(.26f, .45f), "Upper hand on timber");
                    Assert.That(Vector3.Dot(upper - lower, axis), Is.InRange(.26f, .31f), "Hands spaced along timber");
                    if (t == 2f) firstStroke = worker.Bristles.position;
                    if (t == 3f) Assert.That(Vector3.Distance(firstStroke, worker.Bristles.position), Is.GreaterThan(.55f));
                    if (t == 3f || t == 14f)
                    {
                        Assert.That(Physics.Raycast(worker.Bristles.position + Vector3.up * .15f, Vector3.down,
                            out var hit, .25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                        Assert.That(Vector3.Distance(hit.point, worker.Bristles.position), Is.LessThan(.035f));
                    }
                    CheckFingersAroundTimber(worker, hands, axis, fingerErrors);
                    if (t == 2f || t == 3f || t == 3.5f || t == 14f)
                    {
                        string shot = (previous == null ? "first-" : "second-") + t;
                        if (t == 3f || t == 14f) CaptureCurrentCamera(city.Camera, "CityJanitors", shot, 960, 540);
                        Pose wide = new Pose(view.position, view.rotation);
                        Vector3 centre = worker.Broom.position + (upper + lower) * .5f;
                        Vector3 close = centre + worker.Body.transform.forward * .72f + worker.Body.transform.right * .23f + Vector3.up * .11f;
                        view.SetPositionAndRotation(close, Quaternion.LookRotation(centre - close));
                        CaptureCurrentCamera(city.Camera, "CityJanitors", shot + "-hands", 960, 540);
                        view.SetPositionAndRotation(wide.position, wide.rotation);
                    }
                }
                director.Advance(100f);
                director.Advance(1f);
                Assert.That(worker.Active, Is.True, "Keep working after the visit expires while watched.");
                previous = worker;
            }
            Assert.That(fingerErrors, Is.Empty, string.Join("\n", fingerErrors));
        }

        private static void CheckFingersAroundTimber(CityJanitorWorker worker, NpcHandPose hands, Vector3 axis,
            List<string> errors)
        {
            var mesh = worker.Broom.GetComponent<MeshFilter>();
            var points = mesh.sharedMesh.vertices.Select(v => mesh.transform.TransformPoint(v) - worker.Broom.position).ToArray();
            float top = points.Max(p => Vector3.Dot(p, axis));
            Vector3 end = Vector3.zero;
            int topVertices = 0;
            foreach (var p in points)
                if (Vector3.Dot(p, axis) > top - .001f) { end += p; topVertices++; }
            end = worker.Broom.position + end / topVertices;
            foreach (var hand in hands.Hands)
            {
                int digits = 0;
                foreach (var part in hand.Renderers)
                {
                    if (!part.enabled || !part.name.StartsWith("GEO_") ||
                        !(part.name.Contains("Finger") || part.name.Contains("Thumb"))) continue;
                    float distance = CityPedestrianShoeContacts.SkinWorldVertices(part)
                        .Min(v => Vector3.ProjectOnPlane(v - end, axis).magnitude);
                    if (distance < hands.CylinderRadius - .012f || distance > hands.CylinderRadius + .014f)
                        errors.Add($"{part.name} is {distance:F4} m from the centre of the timber at {worker.Feet}.");
                    digits++;
                }
                Assert.That(digits, Is.EqualTo(5), "Check the visible thumb and each curled finger.");
            }
        }
    }
}
