using System;
using System.Collections;
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
        private enum OrdinaryWomenReview { Street, LooseHair, Walk }

        [UnityTest]
        [Explicit("Review the mixed street cast under the real City camera, light and fog.")]
        [PrebuildSetup(typeof(DefaultNpcWomanAssetsSetup))]
        [Timeout(240000)]
        public IEnumerator OrdinaryWomenStreet() => CaptureOrdinaryWomenStreet(OrdinaryWomenReview.Street);

        [UnityTest]
        [Explicit("Review the connected loose hairstyle in profile and from behind.")]
        [PrebuildSetup(typeof(DefaultNpcWomanAssetsSetup))]
        [Timeout(240000)]
        public IEnumerator OrdinaryWomenLooseHair() => CaptureOrdinaryWomenStreet(OrdinaryWomenReview.LooseHair);

        [UnityTest]
        [Explicit("Review the female gait and hip clearance on the live street animation graph.")]
        [PrebuildSetup(typeof(DefaultNpcWomanAssetsSetup))]
        [Timeout(240000)]
        public IEnumerator OrdinaryWomenWalk() => CaptureOrdinaryWomenStreet(OrdinaryWomenReview.Walk);

        private IEnumerator CaptureOrdinaryWomenStreet(OrdinaryWomenReview review)
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt((review == OrdinaryWomenReview.Walk ? 12 : 17) * 60), Is.True);
            var load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds * 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool wasFollowing = follow != null && follow.enabled;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFov = camera.fieldOfView;
            var hero = city.Player.GameObject.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled).ToArray();
            foreach (Renderer renderer in hero) renderer.enabled = false;
            if (follow != null) follow.enabled = false;
            try
            {
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    if (review == OrdinaryWomenReview.Walk)
                    {
                        CaptureOrdinaryWomenWalk(city, camera);
                        yield break;
                    }
                    PlaceResidentLifeStreet(city);
                    yield return null;
                    AssertResidentLifePool(city.Pedestrians);
                    var women = city.Pedestrians.Actors.Where(IsOrdinaryWoman).ToArray();
                    Assert.That(women, Is.Not.Empty, "The scheduled street should present women from the permanent roster.");
                    if (review == OrdinaryWomenReview.LooseHair)
                    {
                        var wearer = women.FirstOrDefault(actor => Wardrobe(actor).GetEquippedItem(NpcWardrobe.HairStyleSlot) == "hair.loose" &&
                            Wardrobe(actor).GetEquippedItem("headwear") == null);
                        Assert.That(wearer, Is.Not.Null, "The close-up needs visible loose hair.");
                        yield return CaptureLooseHair(camera, wearer);
                        yield break;
                    }
                    var distinct = women.GroupBy(actor => Wardrobe(actor).GetEquippedItem(NpcWardrobe.HairStyleSlot));
                    foreach (var group in distinct)
                    {
                        var actor = group.OrderBy(candidate => Wardrobe(candidate).GetEquippedItem("headwear") != null).First();
                        string style = group.Key.Substring("hair.".Length);
                        yield return Frame(actor.Position, actor.Presentation.transform.rotation, style + "-front", false);
                        yield return Frame(actor.Position, actor.Presentation.transform.rotation, style + "-back", true);
                    }

                    // The same outfit and pavement make the night comparison
                    // independent of who happens to be walking at that hour.
                    var reference = women[0];
                    Vector3 position = reference.Position;
                    Quaternion rotation = reference.Presentation.transform.rotation;
                    string id = reference.Presentation.Registry.GetComponent<CityPedestrianDefaultNpcBody>().CharacterId;
                    city.Pedestrians.enabled = false;
                    AdvanceResidentLifeClockTo(22 * 60 + 30);
                    var model = CityPedestrianDefaultNpcBody.Create(city.transform, id, null);
                    try
                    {
                        model.transform.SetPositionAndRotation(position, rotation);
                        var body = model.gameObject.AddComponent<CityPedestrianPresentation>();
                        body.Initialize(model);
                        body.SetMoving(false, true);
                        yield return Frame(position, rotation, "night", false);
                    }
                    finally { Object.DestroyImmediate(model.gameObject); }
                }
            }
            finally
            {
                foreach (Renderer renderer in hero) if (renderer != null) renderer.enabled = true;
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                camera.fieldOfView = originalFov;
                if (follow != null) follow.enabled = wasFollowing;
                city.Pedestrians.enabled = true;
            }

            IEnumerator Frame(Vector3 origin, Quaternion rotation, string name, bool rear)
            {
                Vector3 target = origin + Vector3.up * (rear ? 1.25f : .92f);
                Vector3 position = origin + rotation * (rear ? new Vector3(1.1f, 1.54f, -1.6f) : new Vector3(.85f, 1.27f, 2.7f));
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                camera.fieldOfView = 43f;
                yield return null;
                CaptureCurrentCamera(camera, "OrdinaryWomen", name);
            }
        }

        private static void CaptureOrdinaryWomenWalk(CityGameRoot city, Camera camera)
        {
            CityPedestrianPlan plan = city.PedestrianPlan;
            Vector3 hero = city.Player.GameObject.transform.position;
            CityPedestrianLink lane = plan.Links.Where(link => link.Kind == CityPedestrianLinkKind.Sidewalk && link.Path?.Length >= 8f)
                .OrderBy(link => (plan.Nodes[link.FirstNodeIndex].Position - hero).sqrMagnitude).First();
            CityRoadSample middle = lane.Path.SampleDistance(lane.Path.Length * .5f);
            float ground = lane.PathHeightSampler?.Invoke(middle.Position) ??
                (plan.Nodes[lane.FirstNodeIndex].Position.y + plan.Nodes[lane.SecondNodeIndex].Position.y) * .5f;
            Vector3 feet = new Vector3(middle.Position.x, ground, middle.Position.y);
            Quaternion facing = Quaternion.LookRotation(new Vector3(middle.Tangent.x, 0f, middle.Tangent.y));

            var resident = DefaultNpcPopulation.GetAssignments().First(item => item.ModelId == DefaultNpcCatalog.OrdinaryWoman &&
                item.CharacterId.StartsWith(DefaultNpcPopulation.PedestrianIdPrefix, StringComparison.Ordinal) &&
                item.ItemIds.Contains("trousers.everyday") && !item.ItemIds.Contains("gloves.work"));
            var ambient = Enumerable.Range(0, DefaultNpcPopulation.AmbientAppearancesPerGender)
                .Select(index => DefaultNpcPopulation.GetAssignment(DefaultNpcPopulation.AmbientPedestrianId(index, true)))
                .First(item => item.ItemIds.Contains("trousers.work") && item.ItemIds.Contains("gloves.work"));
            var registry = CityPedestrianDefaultNpcBody.Create(city.transform, resident, null);
            var streetBody = registry.gameObject.AddComponent<CityPedestrianPresentation>();
            streetBody.Initialize(registry);
            Check(streetBody, 0f, "resident");
            Check(CityPedestrianFactory.CreateAmbientPresentation(city.transform, ambient.CharacterId), 1f, "ambient");

            void Check(CityPedestrianPresentation body, float build, string label)
            {
                try
                {
                    body.transform.SetPositionAndRotation(feet, facing);
                    body.gameObject.SetActive(true);
                    var model = body.Registry;
                    Assert.That(model.RoamingWalkClip.name, Is.EqualTo("OrdinaryWomanWalk"));
                    Assert.That(model.RoamingWalkClip.isLooping, Is.True);
                    model.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var shape = model.GetComponent<CityPedestrianDefaultNpcBody>().Character.GetComponent<DefaultNpcBodyShape>();
                    shape.Apply(shape.HeightCm, build);
                    var trousers = model.Renderers.Single(renderer => renderer.name.StartsWith("CLO_Trousers_", StringComparison.Ordinal));
                    Renderer[] arms = model.Renderers.Where(renderer => renderer.name.StartsWith("GEO_Hand", StringComparison.Ordinal) ||
                        renderer.name.StartsWith("CLO_Glove", StringComparison.Ordinal) || renderer.name.Contains("Sleeve.") ||
                        renderer.name.Contains("Cuff.")).ToArray();
                    Assert.That(arms, Is.Not.Empty);
                    using var contacts = new ScarfContactProbe { Origin = feet };
                    var report = new ScarfContactReport();
                    body.SetMoving(true);
                    for (int step = 0; step < 32; step++)
                    {
                        float phase = step / 32f;
                        body.ConfigureCycle(1f, phase);
                        CheckClearance("walk " + phase);
                    }
                    float first = Frame(.25f, "left");
                    float second = Frame(.75f, "right");
                    Assert.That(Mathf.Abs(first - second), Is.InRange(.016f, .034f), "Hips transfer weight with the two steps.");

                    body.SetMoving(false);
                    body.ConfigureCycle(1f, .28f);
                    CheckClearance("idle");
                    for (int step = 0; step < 4; step++)
                    {
                        body.Advance(CityPedestrianPresentation.LocomotionBlendDuration * .25f, true);
                        CheckClearance("start walking " + step);
                    }

                    void CheckClearance(string phase)
                    {
                        var hips = contacts.Read(trousers);
                        foreach (Renderer arm in arms)
                            Assert.That(contacts.Intersects(contacts.Read(arm), hips, phase, report), Is.False,
                                $"{label} {phase}, {arm.name} passes through {trousers.name}");
                    }

                    float Frame(float phase, string side)
                    {
                        body.ConfigureCycle(1f, phase);
                        Vector3 target = feet + Vector3.up * (shape.HeightCm * .005f);
                        Vector3 eye = feet + facing * new Vector3(.65f, 1.10f, 2.30f);
                        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                        camera.fieldOfView = 44f;
                        CaptureCurrentCamera(camera, "OrdinaryWomenWalk", label + "-" + side);
                        return Vector3.Dot(model.PelvisAnchor.position - feet, body.transform.right);
                    }
                }
                finally { Object.DestroyImmediate(body.gameObject); }
            }
        }

        private static IEnumerator CaptureLooseHair(Camera camera, CityPedestrianActor wearer)
        {
            var hairstyle = Wardrobe(wearer).Items.Single(item => item.Id == "hair.loose");
            Assert.That(hairstyle.Parts.Count, Is.EqualTo(1));
            Assert.That(hairstyle.Parts[0].Renderer.enabled, Is.True);
            Transform pose = wearer.Presentation.transform;
            Vector3 focus = wearer.Presentation.Registry.HeadAnchor.position - Vector3.up * .050f - pose.forward * .055f;
            Vector3 side = pose.right;
            if (Physics.Linecast(focus, focus + side * 1.18f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                side = -side;
            camera.fieldOfView = 34f;
            yield return Frame("loose-side", side * 1.18f + Vector3.up * .11f);
            yield return Frame("loose-back", -pose.forward * 1.18f + Vector3.up * .15f);

            IEnumerator Frame(string name, Vector3 offset)
            {
                camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset));
                yield return null;
                CaptureCurrentCamera(camera, "OrdinaryWomen/Closeups", name);
            }
        }

        private static bool IsOrdinaryWoman(CityPedestrianActor actor) => actor.IsSpawned &&
            actor.Presentation != null && actor.Presentation.Registry.DesignId == DefaultNpcCatalog.OrdinaryWoman;

        private static NpcWardrobe Wardrobe(CityPedestrianActor actor) => actor.Presentation.Registry
            .GetComponent<CityPedestrianDefaultNpcBody>().Character.GetComponent<NpcWardrobe>();
    }
}
