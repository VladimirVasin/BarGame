using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarDrinkServiceWorldBuilderPlayModeTests
    {
        private GameObject owner;
        private BarDrinkServicePlan plan;
        private BarDrinkServiceView view;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            owner = new GameObject("Bar Drink Builder Test Owner");
            BarInteriorLayoutPlan layout =
                BarInteriorLayoutPlanner.Generate(
                    20260731,
                    "bar-drink-builder-test",
                    BarActivityKind.Cocktail);
            plan = BarDrinkServicePlan.FromLayout(layout);
            view = BarDrinkServiceWorldBuilder.Build(
                owner.transform,
                plan);

            // Collider-free primitives release their temporary primitive
            // colliders at the end of the creation frame.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (owner != null)
            {
                Object.Destroy(owner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Build_CreatesNinePhysicalSelectableBottles()
        {
            Assert.That(view, Is.Not.Null);
            Assert.That(view.Plan, Is.SameAs(plan));
            Assert.That(
                view.Bottles,
                Has.Count.EqualTo(
                    BarDrinkServicePlan.RequiredBottleCount));
            Assert.That(
                view.Bottles,
                Has.Count.EqualTo(BarDrinkCatalog.Offers.Count));
            Assert.That(view.Vessels, Has.Count.EqualTo(5));
            Assert.That(
                view.GetComponentsInChildren<Light>(true),
                Is.Empty,
                "The physical service must reuse existing bar lighting.");
            Assert.That(
                view.GetComponentsInChildren<Rigidbody>(true),
                Has.Length.EqualTo(BarDrinkServicePlan.RequiredBottleCount));
            Assert.That(
                view.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(
                    BarDrinkServicePlan.RequiredBottleCount * 2));

            var drinkIds = new HashSet<DrinkId>();
            var slotIds = new HashSet<string>();
            for (int index = 0; index < view.Bottles.Count; index++)
            {
                BarDrinkBottleView bottle = view.Bottles[index];
                BarDrinkOffer offer = BarDrinkCatalog.Offers[index];
                BarDrinkBottleSlotPlan slot = plan.BottleSlots[index];
                BarDrinkPresentation presentation =
                    BarDrinkPresentationCatalog.Get(offer.DrinkId);

                Assert.That(bottle, Is.Not.Null, offer.DrinkId.ToString());
                Assert.That(bottle.IsInitialized, Is.True);
                Assert.That(bottle.DrinkId, Is.EqualTo(offer.DrinkId));
                Assert.That(bottle.DrinkId, Is.EqualTo(slot.DrinkId));
                Assert.That(bottle.SlotId, Is.EqualTo(slot.Id));
                Assert.That(drinkIds.Add(bottle.DrinkId), Is.True);
                Assert.That(slotIds.Add(bottle.SlotId), Is.True);
                Assert.That(
                    bottle.transform.IsChildOf(view.transform),
                    Is.True);
                Assert.That(
                    bottle.transform.parent.localPosition,
                    Is.EqualTo(slot.Pose.Position));
                Assert.That(bottle.Renderers, Is.Not.Empty);
                Assert.That(bottle.MouthAnchor, Is.Not.Null);
                Assert.That(
                    bottle.MouthAnchor.IsChildOf(bottle.transform),
                    Is.True);
                Assert.That(
                    bottle.MouthAnchor.localPosition.y,
                    Is.GreaterThan(0.5f));

                Assert.That(bottle.SolidCollider, Is.Not.Null);
                Assert.That(bottle.SolidCollider.enabled, Is.True);
                Assert.That(bottle.SolidCollider.isTrigger, Is.False);
                Assert.That(
                    bottle.SolidCollider.transform,
                    Is.SameAs(bottle.transform));
                Assert.That(bottle.SelectionTrigger, Is.Not.Null);
                Assert.That(bottle.SelectionTrigger.enabled, Is.True);
                Assert.That(bottle.SelectionTrigger.isTrigger, Is.True);
                Assert.That(
                    bottle.SelectionTrigger.transform,
                    Is.SameAs(bottle.transform));
                Assert.That(
                    bottle.SelectionTrigger,
                    Is.Not.SameAs(bottle.SolidCollider));
                AssertBoundsContain(
                    bottle.SelectionTrigger.bounds,
                    bottle.SolidCollider.bounds,
                    offer.DrinkId.ToString());

                Assert.That(bottle.Body, Is.Not.Null);
                Assert.That(bottle.Body.transform, Is.SameAs(bottle.transform));
                Assert.That(bottle.Body.isKinematic, Is.True);
                Assert.That(bottle.Body.useGravity, Is.False);
                Assert.That(bottle.Body.detectCollisions, Is.True);
                Assert.That(
                    bottle.Body.collisionDetectionMode,
                    Is.EqualTo(
                        CollisionDetectionMode.ContinuousSpeculative));

                if (presentation.BottleStyle ==
                    BarDrinkBottleStyle.VodkaBottle)
                {
                    Assert.That(
                        bottle.SolidCollider,
                        Is.TypeOf<BoxCollider>());
                }
                else
                {
                    Assert.That(
                        bottle.SolidCollider,
                        Is.TypeOf<CapsuleCollider>());
                }

                Assert.That(
                    bottle.SelectionTrigger,
                    Is.TypeOf<BoxCollider>());
                Assert.That(
                    view.TryGetBottle(
                        bottle.DrinkId,
                        out BarDrinkBottleView byDrink),
                    Is.True);
                Assert.That(byDrink, Is.SameAs(bottle));
                Assert.That(
                    view.TryGetBottle(
                        bottle.SelectionTrigger,
                        out BarDrinkBottleView byTrigger),
                    Is.True);
                Assert.That(byTrigger, Is.SameAs(bottle));
                Assert.That(
                    view.TryGetBottle(
                        bottle.SolidCollider,
                        out BarDrinkBottleView bySolid),
                    Is.True);
                Assert.That(bySolid, Is.SameAs(bottle));
            }

            Assert.That(drinkIds.Contains(DrinkId.None), Is.False);
            Assert.That(drinkIds.Contains(DrinkId.Moonshine), Is.False);
            AssertBottleRowFitsNarrowWidescreen();
            AssertSharedMaterialsHaveNoInstances(view.transform);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            VesselMapping_StreamFillAndReset_UseRealThreeDimensionalObjects()
        {
            var vesselKinds = new HashSet<BarDrinkVesselKind>();
            var originalVesselScales =
                new Dictionary<BarDrinkVesselKind, Vector3>();
            for (int index = 0; index < view.Vessels.Count; index++)
            {
                BarDrinkVesselView vessel = view.Vessels[index];
                Assert.That(vesselKinds.Add(vessel.Kind), Is.True);
                Assert.That(vessel.Kind, Is.Not.EqualTo(BarDrinkVesselKind.None));
                Assert.That(vessel.GlassRenderer, Is.Not.Null);
                Assert.That(vessel.LiquidRenderer, Is.Not.Null);
                Assert.That(vessel.LiquidRoot, Is.Not.Null);
                Assert.That(vessel.PourTarget, Is.Not.Null);
                Assert.That(vessel.gameObject.activeSelf, Is.False);
                Assert.That(vessel.FillProgress, Is.Zero);
                originalVesselScales.Add(
                    vessel.Kind,
                    vessel.transform.localScale);
                Assert.That(
                    vessel.GlassRenderer.sharedMaterial,
                    Is.SameAs(BarDrinkServiceResources.GlassMaterial));
                Assert.That(
                    vessel.LiquidRenderer.sharedMaterial,
                    Is.SameAs(BarDrinkServiceResources.LiquidMaterial));
            }

            Assert.That(
                vesselKinds,
                Is.EquivalentTo(new[]
                {
                    BarDrinkVesselKind.Tumbler,
                    BarDrinkVesselKind.Pint,
                    BarDrinkVesselKind.WineGlass,
                    BarDrinkVesselKind.ShotGlass,
                    BarDrinkVesselKind.Snifter
                }));
            Assert.That(
                view.StreamRenderer.sharedMaterial,
                Is.SameAs(BarDrinkServiceResources.LiquidMaterial));
            Assert.That(view.IsStreamVisible, Is.False);

            BarDrinkBottleView finalBottle =
                view.Bottles[view.Bottles.Count - 1];
            Transform originalParent = finalBottle.transform.parent;
            Vector3 originalLocalPosition =
                finalBottle.transform.localPosition;
            Quaternion originalLocalRotation =
                finalBottle.transform.localRotation;
            Vector3 originalLocalScale = finalBottle.transform.localScale;

            for (int index = 0;
                 index < BarDrinkPresentationCatalog.Presentations.Count;
                 index++)
            {
                BarDrinkPresentation presentation =
                    BarDrinkPresentationCatalog.Presentations[index];
                Assert.That(
                    view.SelectBottle(presentation.DrinkId),
                    Is.True,
                    presentation.DrinkId.ToString());
                Assert.That(
                    view.ShowVesselForDrink(presentation.DrinkId),
                    Is.True,
                    presentation.DrinkId.ToString());

                BarDrinkVesselView vessel = view.ActiveVessel;
                Assert.That(vessel, Is.Not.Null);
                Assert.That(
                    Vector3.Distance(
                        vessel.transform.localScale,
                        originalVesselScales[vessel.Kind]),
                    Is.LessThan(0.0001f),
                    $"{presentation.DrinkId} reused a scaled vessel.");
                Assert.That(
                    vessel.Kind,
                    Is.EqualTo(presentation.VesselKind));
                Assert.That(vessel.gameObject.activeSelf, Is.True);
                Assert.That(
                    vessel.TargetFill,
                    Is.EqualTo(presentation.TargetFill).Within(0.0001f));
                Assert.That(vessel.FillProgress, Is.Zero);
                Assert.That(vessel.LiquidRoot.gameObject.activeSelf, Is.False);

                view.SetFillProgress(0.5f);
                Assert.That(vessel.FillProgress, Is.EqualTo(0.5f));
                Assert.That(
                    vessel.DisplayedFill,
                    Is.EqualTo(presentation.TargetFill * 0.5f)
                        .Within(0.0001f));
                Assert.That(vessel.LiquidRoot.gameObject.activeSelf, Is.True);

                Assert.That(
                    view.SetPourStreamFromBottle(
                        presentation.LiquidColor,
                        0.012f),
                    Is.True);
                Assert.That(view.IsStreamVisible, Is.True);
                Assert.That(
                    view.StreamRoot.localScale.x,
                    Is.EqualTo(0.012f).Within(0.0001f));
                Assert.That(
                    view.StreamRoot.localScale.y,
                    Is.GreaterThan(0.0025f));
                view.HidePourStream();
                Assert.That(view.IsStreamVisible, Is.False);

                // The service animation scales a vessel down while it enters
                // and exits. Reusing the same kind must always start from the
                // authored scale instead of compounding that animation scale.
                vessel.transform.localScale *= 0.02f;
            }

            view.SetSelectedBottleWorldPose(
                new Vector3(12f, 4f, -8f),
                Quaternion.Euler(17f, 29f, 43f));
            Assert.That(finalBottle.SolidCollider.enabled, Is.False);
            Assert.That(finalBottle.SelectionTrigger.enabled, Is.False);
            Assert.That(
                Vector3.Distance(
                    finalBottle.transform.position,
                    new Vector3(12f, 4f, -8f)),
                Is.LessThan(0.0001f));
            Assert.That(view.ActiveVessel, Is.Not.Null);
            Assert.That(view.ActiveVessel.FillProgress, Is.EqualTo(0.5f));

            view.ResetPresentation();

            Assert.That(view.SelectedBottle, Is.Null);
            Assert.That(view.ActiveVessel, Is.Null);
            Assert.That(view.IsStreamVisible, Is.False);
            Assert.That(finalBottle.transform.parent, Is.SameAs(originalParent));
            Assert.That(
                finalBottle.transform.localPosition,
                Is.EqualTo(originalLocalPosition));
            Assert.That(
                Quaternion.Angle(
                    finalBottle.transform.localRotation,
                    originalLocalRotation),
                Is.LessThan(0.01f));
            Assert.That(
                finalBottle.transform.localScale,
                Is.EqualTo(originalLocalScale));
            Assert.That(finalBottle.SolidCollider.enabled, Is.True);
            Assert.That(finalBottle.SelectionTrigger.enabled, Is.True);
            Assert.That(finalBottle.Body.isKinematic, Is.True);
            Assert.That(finalBottle.Body.useGravity, Is.False);
            for (int index = 0; index < view.Vessels.Count; index++)
            {
                BarDrinkVesselView vessel = view.Vessels[index];
                Assert.That(vessel.gameObject.activeSelf, Is.False);
                Assert.That(vessel.FillProgress, Is.Zero);
                Assert.That(
                    Vector3.Distance(
                        vessel.transform.localScale,
                        originalVesselScales[vessel.Kind]),
                    Is.LessThan(0.0001f));
            }

            yield return null;
        }

        private void AssertBottleRowFitsNarrowWidescreen()
        {
            Assert.That(plan.CameraPosition.y, Is.InRange(1.70f, 1.85f));
            Assert.That(
                plan.CameraLookAt.y - plan.CameraPosition.y,
                Is.InRange(0.25f, 0.45f));
            var cameraObject = new GameObject(
                "Bar Drink Framing Test Camera");
            cameraObject.transform.SetParent(owner.transform, false);
            Camera framingCamera = cameraObject.AddComponent<Camera>();
            framingCamera.enabled = false;
            framingCamera.aspect = 16f / 10f;
            framingCamera.fieldOfView = plan.CameraFieldOfView;
            framingCamera.transform.SetPositionAndRotation(
                plan.CameraPosition,
                plan.CameraRotation);

            const float horizontalSafeMargin = 0.025f;
            for (int bottleIndex = 0;
                 bottleIndex < view.Bottles.Count;
                 bottleIndex++)
            {
                BarDrinkBottleView bottle = view.Bottles[bottleIndex];
                for (int rendererIndex = 0;
                     rendererIndex < bottle.Renderers.Count;
                     rendererIndex++)
                {
                    Bounds bounds = bottle.Renderers[rendererIndex].bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 signs = new Vector3(
                            (corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f);
                        Vector3 worldCorner = bounds.center +
                            Vector3.Scale(bounds.extents, signs);
                        Vector3 viewport =
                            framingCamera.WorldToViewportPoint(worldCorner);
                        Assert.That(
                            viewport.z,
                            Is.GreaterThan(0f),
                            bottle.DrinkId.ToString());
                        Assert.That(
                            viewport.x,
                            Is.InRange(
                                horizontalSafeMargin,
                                1f - horizontalSafeMargin),
                            $"{bottle.DrinkId} is clipped at 16:10.");
                        Assert.That(
                            viewport.y,
                            Is.InRange(0.10f, 0.90f),
                            $"{bottle.DrinkId} is vertically clipped.");
                    }
                }
            }
        }

        private static void AssertBoundsContain(
            Bounds outer,
            Bounds inner,
            string description)
        {
            const float tolerance = 0.0001f;
            Assert.That(
                outer.min.x,
                Is.LessThanOrEqualTo(inner.min.x + tolerance),
                description);
            Assert.That(
                outer.min.y,
                Is.LessThanOrEqualTo(inner.min.y + tolerance),
                description);
            Assert.That(
                outer.min.z,
                Is.LessThanOrEqualTo(inner.min.z + tolerance),
                description);
            Assert.That(
                outer.max.x,
                Is.GreaterThanOrEqualTo(inner.max.x - tolerance),
                description);
            Assert.That(
                outer.max.y,
                Is.GreaterThanOrEqualTo(inner.max.y - tolerance),
                description);
            Assert.That(
                outer.max.z,
                Is.GreaterThanOrEqualTo(inner.max.z - tolerance),
                description);
        }

        private static void AssertSharedMaterialsHaveNoInstances(
            Transform root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            for (int index = 0; index < renderers.Length; index++)
            {
                Assert.That(renderers[index].sharedMaterial, Is.Not.Null);
                Assert.That(
                    renderers[index].sharedMaterial.name,
                    Does.Not.Contain("(Instance)"));
            }
        }
    }
}
