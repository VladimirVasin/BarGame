using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarDrinkServiceWorldBuilderPlayModeTests
    {
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int SurfaceAlbedoCompensationId =
            Shader.PropertyToID("_SurfaceAlbedoCompensation");

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
        public IEnumerator Build_CreatesFourPhysicalSelectableBottles()
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

                bottle.SetSelectionHighlight(1f);
                bottle.SetSelectionHighlight(0f);
                AssertUsesOpaqueSheet(
                    bottle.Renderers[0],
                    BarSurfaceKind.BottleGlass,
                    presentation.BottleColor);
                AssertUsesOpaqueSheet(
                    bottle.Renderers[1],
                    BarSurfaceKind.PaintedMetal);
                AssertUsesOpaqueSheet(
                    bottle.Renderers[2],
                    BarSurfaceKind.Paper,
                    presentation.LabelColor);
            }

            Assert.That(drinkIds.Contains(DrinkId.None), Is.False);
            Assert.That(drinkIds.Contains(DrinkId.Moonshine), Is.False);
            AssertBottleRowFitsNarrowWidescreen();
            AssertSharedMaterialsHaveNoInstances(view.transform);
            yield return null;
        }

        [UnityTest]
        public IEnumerator
            CarriedBottle_UsesSurfaceGripAndLeavesShelfSourceExact()
        {
            BarDrinkPresentation presentation =
                BarDrinkPresentationCatalog.Get(DrinkId.RedWine);
            Assert.That(view.SelectBottle(presentation.DrinkId), Is.True);
            BarDrinkBottleView source = view.SelectedBottle;
            Transform sourceParent = source.transform.parent;
            Vector3 sourceLocalPosition = source.transform.localPosition;
            Quaternion sourceLocalRotation = source.transform.localRotation;
            Vector3 sourceLocalScale = source.transform.localScale;
            Vector3 originalLocalPosition = source.OriginalLocalPosition;
            Quaternion originalLocalRotation = source.OriginalLocalRotation;
            Vector3 originalLocalScale = source.OriginalLocalScale;

            var carrierObject = new GameObject("Bottle Grip Test Carrier");
            carrierObject.transform.SetParent(owner.transform, false);
            carrierObject.transform.position = new Vector3(1.2f, 1.4f, 0.8f);
            carrierObject.transform.localScale = Vector3.one * 100f;
            Assert.That(
                view.ShowCarriedBottle(
                    presentation,
                    carrierObject.transform),
                Is.True);
            view.AlignCarriedBottleToCarrier(
                carrierObject.transform,
                Quaternion.identity,
                carrierObject.transform.position + Vector3.back);

            Assert.That(view.IsCarriedBottleVisible, Is.True);
            Assert.That(
                view.CarriedBottleRoot.parent,
                Is.SameAs(view.transform),
                "The prop must not inherit the imported socket's 100x scale.");
            Assert.That(
                view.CarriedBottleRoot.lossyScale,
                Is.EqualTo(Vector3.one *
                    BarDrinkServiceView.CarriedBottleScale));
            Assert.That(
                view.CarriedBottleRoot.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                view.CarriedBottleRoot.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                view.CarriedBottleRoot.GetComponentsInChildren<
                    BarDrinkBottleView>(true),
                Is.Empty);
            Assert.That(
                Vector3.Distance(
                    view.CarriedBottleHandContactWorldPosition,
                    carrierObject.transform.position),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(
                    view.CarriedBottleGripCenterWorldPosition,
                    carrierObject.transform.position),
                Is.LessThan(0.0001f),
                "The hand's authored curled-finger cylinder must surround " +
                "the actual bottle axis, without a radial socket offset.");

            Vector3 mouthBeforeCarrierMove =
                view.CarriedBottleMouthWorldPosition;
            Vector3 carrierMove = new Vector3(0.18f, -0.04f, 0.11f);
            carrierObject.transform.position += carrierMove;
            view.AlignCarriedBottleToCarrier(
                carrierObject.transform,
                Quaternion.identity,
                carrierObject.transform.position + Vector3.back);
            Assert.That(
                Vector3.Distance(
                    view.CarriedBottleHandContactWorldPosition,
                    carrierObject.transform.position),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(
                    view.CarriedBottleMouthWorldPosition -
                    mouthBeforeCarrierMove,
                    carrierMove),
                Is.LessThan(0.0001f));

            Quaternion horizontal = Quaternion.Euler(0f, 0f, 90f);
            view.AlignCarriedBottleToCarrier(
                carrierObject.transform,
                horizontal,
                carrierObject.transform.position + Vector3.back);
            Assert.That(Vector3.Distance(
                    view.CarriedBottleHandContactWorldPosition,
                    view.CarriedBottleGripCenterWorldPosition),
                Is.LessThan(0.0001f),
                "Tilting must keep the physical grip on the bottle axis.");
            Vector3 horizontalRadial = view.ResolveCarriedBottleHandRadial(
                view.CarriedBottleRoot.position, horizontal,
                carrierObject.transform.position + Vector3.back);
            Assert.That(
                Vector3.Dot(horizontalRadial.normalized, Vector3.down),
                Is.GreaterThan(0.99f),
                "A horizontal bottle's palm-facing direction belongs below " +
                "the bottle; this orientation must not offset its grip centre.");

            Assert.That(source.transform.parent, Is.SameAs(sourceParent));
            Assert.That(source.transform.localPosition,
                Is.EqualTo(sourceLocalPosition));
            Assert.That(
                Quaternion.Angle(
                    source.transform.localRotation,
                    sourceLocalRotation),
                Is.LessThan(0.001f));
            Assert.That(source.transform.localScale,
                Is.EqualTo(sourceLocalScale));
            Assert.That(source.SolidCollider.enabled, Is.True);
            Assert.That(source.SelectionTrigger.enabled, Is.True);
            for (int index = 0; index < source.Renderers.Count; index++)
            {
                Assert.That(source.Renderers[index].enabled, Is.False);
            }

            Assert.That(
                view.ShowVesselForDrink(presentation.DrinkId),
                Is.True);
            Assert.That(
                view.SetPourStreamFromCarriedBottle(
                    presentation.LiquidColor),
                Is.True);
            Assert.That(view.IsStreamVisible, Is.True);
            Assert.That(
                Vector3.Distance(
                    view.StreamRoot.TransformPoint(Vector3.down),
                    view.CarriedBottleMouthWorldPosition),
                Is.LessThan(0.0001f),
                "The pour stream must start at the carried mouth anchor.");

            view.ResetPresentation();
            Assert.That(view.IsCarriedBottleVisible, Is.False);
            Assert.That(view.CarriedBottleRoot, Is.Null);
            Assert.That(source.transform.parent, Is.SameAs(sourceParent));
            Assert.That(source.transform.localPosition,
                Is.EqualTo(originalLocalPosition));
            Assert.That(
                Quaternion.Angle(
                    source.transform.localRotation,
                    originalLocalRotation),
                Is.LessThan(0.001f));
            Assert.That(source.transform.localScale,
                Is.EqualTo(originalLocalScale));
            Assert.That(source.SolidCollider.enabled, Is.True);
            Assert.That(source.SelectionTrigger.enabled, Is.True);
            for (int index = 0; index < source.Renderers.Count; index++)
            {
                Assert.That(source.Renderers[index].enabled, Is.True);
            }

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
                AssertUsesBottleGlassSheet(vessel.GlassRenderer);
                AssertUsesBottleGlassSheet(vessel.LiquidRenderer);
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
            AssertUsesBottleGlassSheet(view.StreamRenderer);
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
                 index < BarDrinkCatalog.Offers.Count;
                 index++)
            {
                BarDrinkPresentation presentation =
                    BarDrinkPresentationCatalog.Get(
                        BarDrinkCatalog.Offers[index].DrinkId);
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

        private static void AssertUsesBottleGlassSheet(Renderer renderer)
        {
            Assert.That(renderer.sharedMaterial.HasProperty(BaseMapId), Is.True);
            Assert.That(
                renderer.sharedMaterial.HasProperty(
                    SurfaceAlbedoCompensationId),
                Is.True);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.SameAs(
                    BarSurfaceAppearance.GetTexture(
                        BarSurfaceKind.BottleGlass)));
            Assert.That(
                properties.GetVector(BaseMapTransformId),
                Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
            Assert.That(
                properties.GetFloat(SurfaceAlbedoCompensationId),
                Is.EqualTo(
                    BarSurfaceAppearance.GetRecipe(
                        BarSurfaceKind.BottleGlass).AlbedoCompensation));
        }

        private static void AssertUsesOpaqueSheet(
            Renderer renderer,
            BarSurfaceKind kind,
            Color? sourceTint = null)
        {
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.SameAs(BarSurfaceAppearance.GetTexture(kind)));
            Assert.That(
                properties.GetVector(BaseMapTransformId),
                Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
            if (!sourceTint.HasValue)
            {
                return;
            }

            Color expected = BarSurfaceAppearance.CreateDisplayTint(
                sourceTint.Value,
                kind);
            Color actual = properties.GetColor(BaseColorId);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.00001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.00001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.00001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.00001f));
        }

        private void AssertBottleRowFitsNarrowWidescreen()
        {
            Assert.That(plan.CameraPosition.y, Is.InRange(1.60f, 1.65f));
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

        internal static void AssertBottleDoesNotIntersectHand(
            NpcHandPose pose, bool isLeft, Transform bottleRoot, string label)
        {
            const float maximumPenetration = .0025f;
            Assert.That(pose, Is.Not.Null, label);
            Assert.That(bottleRoot, Is.Not.Null, label);
            var solids = new List<BottleSolid>();
            BarDrinkVesselView vessel = bottleRoot.GetComponent<BarDrinkVesselView>();
            foreach (MeshFilter filter in bottleRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (vessel != null && renderer == vessel.InteractionHighlightRenderer) continue;
                ReadSolidMesh(filter.sharedMesh, out Vector3[] vertices, out int[] triangles);
                for (int index = 0; index < vertices.Length; index++)
                    vertices[index] = filter.transform.TransformPoint(vertices[index]);
                AddClosedPropPieces(solids, renderer.name, vertices, triangles);
            }
            Assert.That(solids, Is.Not.Empty, label + " has no physical bottle solids");
            NpcHandPose.HandBinding binding = null;
            foreach (NpcHandPose.HandBinding hand in pose.Hands)
                if (hand.IsLeft == isLeft) binding = hand;
            Assert.That(binding, Is.Not.Null, label);

            float deepest = 0f;
            string deepestPart = string.Empty;
            int inspected = 0;
            foreach (SkinnedMeshRenderer renderer in binding.Renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Vector3[] vertices = ReadVerifiedHandWorldVertices(renderer, binding.Hand, label);
                int[] triangles = renderer.sharedMesh.triangles;
                foreach (Vector3 vertex in vertices) Inspect(vertex, renderer.name);
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]],
                        c = vertices[triangles[index + 2]];
                    Inspect((a + b + c) / 3f, renderer.name);
                    Inspect((a + b) * .5f, renderer.name);
                    Inspect((b + c) * .5f, renderer.name);
                    Inspect((c + a) * .5f, renderer.name);
                }
                inspected++;
            }
            Assert.That(inspected, Is.EqualTo(6), label + " must inspect the visible palm and five digits");
            Assert.That(deepest, Is.LessThanOrEqualTo(maximumPenetration),
                $"{label}: visible hand penetrates bottle solid by {deepest:F5} m at {deepestPart}");

            void Inspect(Vector3 point, string handPart)
            {
                foreach (BottleSolid solid in solids)
                {
                    float depth = solid.Penetration(point);
                    if (depth <= deepest) continue;
                    deepest = depth;
                    deepestPart = handPart + " / " + solid.Name;
                }
            }
        }

        internal static float MinimumFingerCurlDegrees(NpcHandGripKind kind) => kind switch
        {
            NpcHandGripKind.Cylindrical => 20f,
            NpcHandGripKind.WineBowl => 12f,
            NpcHandGripKind.SnifterBowl => 10f,
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind))
        };

        internal static float MeasureFingerCurlDegrees(SkinnedMeshRenderer renderer,
            Transform hand, Vector3[] finalWorldVertices)
        {
            Mesh mesh = renderer.sharedMesh;
            int bone = System.Array.IndexOf(renderer.bones, hand);
            Assert.That(bone, Is.GreaterThanOrEqualTo(0), renderer.name);
            Matrix4x4 toWorld = hand.localToWorldMatrix * mesh.bindposes[bone];
            Vector3[] neutral = mesh.vertices;
            Assert.That(finalWorldVertices.Length, Is.EqualTo(neutral.Length), renderer.name);
            Vector3 centre = default;
            for (int index = 0; index < neutral.Length; index++)
            {
                neutral[index] = toWorld.MultiplyPoint3x4(neutral[index]);
                centre += neutral[index];
            }
            centre /= neutral.Length;
            Vector3 variance = default;
            foreach (Vector3 point in neutral)
            {
                Vector3 delta = point - centre;
                variance += Vector3.Scale(delta, delta);
            }
            Vector3 axis = variance.x >= variance.y && variance.x >= variance.z
                ? Vector3.right : variance.y >= variance.z ? Vector3.up : Vector3.forward;
            // The original straight finger's dominant geometric axis supplies
            // proximal/distal ring membership, without authored socket axes or
            // an assumption about imported mesh vertex order.
            for (int iteration = 0; iteration < 10; iteration++)
            {
                Vector3 next = default;
                foreach (Vector3 point in neutral)
                {
                    Vector3 delta = point - centre;
                    next += delta * Vector3.Dot(delta, axis);
                }
                axis = next.normalized;
            }
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (Vector3 point in neutral)
            {
                float along = Vector3.Dot(point - centre, axis);
                minimum = Mathf.Min(minimum, along); maximum = Mathf.Max(maximum, along);
            }
            Vector3 beforeRoot = default, beforeTip = default, afterRoot = default, afterTip = default;
            int roots = 0, tips = 0;
            for (int index = 0; index < neutral.Length; index++)
            {
                float along = Vector3.Dot(neutral[index] - centre, axis);
                if (along <= minimum + .003f)
                {
                    beforeRoot += neutral[index]; afterRoot += finalWorldVertices[index]; roots++;
                }
                if (along >= maximum - .003f)
                {
                    beforeTip += neutral[index]; afterTip += finalWorldVertices[index]; tips++;
                }
            }
            Assert.That(roots, Is.GreaterThan(2), renderer.name);
            Assert.That(tips, Is.GreaterThan(2), renderer.name);
            return Vector3.Angle(beforeTip / tips - beforeRoot / roots,
                afterTip / tips - afterRoot / roots);
        }

        internal static Vector3[] ReadVerifiedHandWorldVertices(
            SkinnedMeshRenderer renderer, Transform hand, string label)
        {
            var baked = new Mesh();
            Vector3[] actual;
            try
            {
                renderer.BakeMesh(baked, false);
                actual = baked.vertices;
                // BakeMesh(false) compensates position/rotation but retains
                // scale in its output. FBX hand vertices are already metres:
                // TransformPoint would apply the imported 100x unit factor
                // a second time. Reapply only the compensated rigid pose.
                Vector3 origin = renderer.transform.position;
                Quaternion rotation = renderer.transform.rotation;
                for (int index = 0; index < actual.Length; index++)
                    actual[index] = origin + rotation * actual[index];
            }
            finally { Object.DestroyImmediate(baked); }

            // Independently skin the small existing hand meshes. A stale native
            // BakeMesh matrix must fail visibly, rather than putting the hand
            // metres away and falsely passing the non-intersection proof.
            Mesh source = renderer.sharedMesh;
            Vector3[] posed = source.vertices;
            var delta = new Vector3[posed.Length];
            var normals = new Vector3[posed.Length];
            var tangents = new Vector3[posed.Length];
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (Mathf.Abs(weight) < .0001f) continue;
                Assert.That(source.GetBlendShapeFrameCount(shape), Is.EqualTo(1), renderer.name);
                source.GetBlendShapeFrameVertices(shape, 0, delta, normals, tangents);
                float blend = weight / source.GetBlendShapeFrameWeight(shape, 0);
                for (int index = 0; index < posed.Length; index++) posed[index] += delta[index] * blend;
            }
            Transform[] bones = renderer.bones;
            Matrix4x4[] bind = source.bindposes;
            var matrices = new Matrix4x4[bones.Length];
            for (int index = 0; index < matrices.Length; index++)
                matrices[index] = bones[index].localToWorldMatrix * bind[index];
            BoneWeight[] weights = source.boneWeights;
            Assert.That(actual.Length, Is.EqualTo(posed.Length), renderer.name);
            float largestError = 0f;
            Vector3 worstExpected = default, worstActual = default;
            for (int index = 0; index < posed.Length; index++)
            {
                BoneWeight weight = weights[index];
                Vector3 expected = Vector3.zero;
                Add(weight.boneIndex0, weight.weight0);
                Add(weight.boneIndex1, weight.weight1);
                Add(weight.boneIndex2, weight.weight2);
                Add(weight.boneIndex3, weight.weight3);
                float error = Vector3.Distance(actual[index], expected);
                if (error > largestError)
                {
                    largestError = error; worstExpected = expected; worstActual = actual[index];
                }
                void Add(int bone, float influence)
                {
                    if (influence > 0f) expected += matrices[bone].MultiplyPoint3x4(posed[index]) * influence;
                }
            }
            Assert.That(largestError, Is.LessThan(.001f),
                $"{label}/{renderer.name}: baked skin differs from live bone/bindpose/shape by {largestError:F5}m; " +
                $"baked={worstActual:F4}, direct={worstExpected:F4}, hand={hand.position:F4}, " +
                $"renderer={renderer.transform.position:F4}, scale={renderer.transform.lossyScale:F4}, " +
                $"rootBone={(renderer.rootBone != null ? renderer.rootBone.position.ToString("F4") : "none")}");
            return actual;
        }

        internal static void ReadSolidMesh(Mesh mesh, out Vector3[] vertices, out int[] triangles)
        {
#if UNITY_EDITOR
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(
                       data[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                Mesh.MeshData value = data[0];
                value.GetVertices(positions);
                vertices = positions.ToArray();
                var indices = new List<int>();
                for (int sub = 0; sub < value.subMeshCount; sub++)
                {
                    UnityEngine.Rendering.SubMeshDescriptor range = value.GetSubMesh(sub);
                    Assert.That(range.topology, Is.EqualTo(MeshTopology.Triangles), mesh.name);
                    if (value.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
                    {
                        var raw = value.GetIndexData<ushort>();
                        for (int index = 0; index < range.indexCount; index++)
                            indices.Add(raw[range.indexStart + index] + range.baseVertex);
                    }
                    else
                    {
                        var raw = value.GetIndexData<uint>();
                        for (int index = 0; index < range.indexCount; index++)
                            indices.Add((int)raw[range.indexStart + index] + range.baseVertex);
                    }
                }
                triangles = indices.ToArray();
            }
#else
            vertices = mesh.vertices;
            triangles = mesh.triangles;
#endif
        }

        internal static void AddClosedPropPieces(List<BottleSolid> solids, string name,
            Vector3[] vertices, int[] triangles)
        {
            var parents = new int[vertices.Length];
            var welded = new Dictionary<Vector3Int, int>();
            for (int index = 0; index < vertices.Length; index++)
            {
                parents[index] = index;
                Vector3Int key = Vector3Int.RoundToInt(vertices[index] * 100000f);
                if (welded.TryGetValue(key, out int previous)) Join(index, previous);
                else welded.Add(key, index);
            }
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Join(triangles[index], triangles[index + 1]);
                Join(triangles[index], triangles[index + 2]);
            }
            var pieces = new Dictionary<int, List<int>>();
            for (int index = 0; index < triangles.Length; index += 3)
            {
                int root = Find(triangles[index]);
                if (!pieces.TryGetValue(root, out List<int> piece))
                    pieces.Add(root, piece = new List<int>());
                piece.Add(triangles[index]); piece.Add(triangles[index + 1]); piece.Add(triangles[index + 2]);
            }
            foreach (List<int> piece in pieces.Values)
                solids.Add(new BottleSolid(name + " piece " + solids.Count, vertices, piece));

            int Find(int index)
            {
                while (parents[index] != index)
                {
                    parents[index] = parents[parents[index]];
                    index = parents[index];
                }
                return index;
            }
            void Join(int a, int b) => parents[Find(a)] = Find(b);
        }

        internal sealed class BottleSolid
        {
            public readonly string Name;
            private readonly Vector3[] surface;
            private readonly Bounds bounds;
            private readonly List<Vector3> clipA = new List<Vector3>(32);
            private readonly List<Vector3> clipB = new List<Vector3>(32);
            public bool Overlaps(Bounds candidate) => bounds.Intersects(candidate);
            public BottleSolid(string name, Vector3[] vertices, List<int> triangles)
            {
                Name = name;
                Bounds measuredBounds = new Bounds(vertices[triangles[0]], Vector3.zero);
                foreach (int index in triangles) measuredBounds.Encapsulate(vertices[index]);
                bounds = measuredBounds;
                var faces = new List<Vector3>();
                for (int index = 0; index < triangles.Count; index += 3)
                {
                    Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]],
                        c = vertices[triangles[index + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-16f) continue;
                    faces.Add(a); faces.Add(b); faces.Add(c);
                }
                surface = faces.ToArray();
                Assert.That(surface.Length, Is.GreaterThanOrEqualTo(12), name);
            }

            // Seat disks, stems, feet and the counter top are convex authored
            // pieces. Clip the entire skin triangle against their inset volume;
            // a large triangle must not jump over a thin seat between vertices.
            public bool IntersectsConvexInterior(Vector3 a, Vector3 b, Vector3 c, float inset)
            {
                Vector3 minimum = Vector3.Min(a, Vector3.Min(b, c));
                Vector3 maximum = Vector3.Max(a, Vector3.Max(b, c));
                if (minimum.x > bounds.max.x || maximum.x < bounds.min.x ||
                    minimum.y > bounds.max.y || maximum.y < bounds.min.y ||
                    minimum.z > bounds.max.z || maximum.z < bounds.min.z) return false;
                clipA.Clear(); clipA.Add(a); clipA.Add(b); clipA.Add(c);
                List<Vector3> input = clipA, output = clipB;
                for (int face = 0; face < surface.Length; face += 3)
                {
                    Vector3 origin = surface[face];
                    Vector3 normal = Vector3.Cross(surface[face + 1] - origin,
                        surface[face + 2] - origin).normalized;
                    if (Vector3.Dot(normal, bounds.center - origin) > 0f) normal = -normal;
                    output.Clear();
                    Vector3 previous = input[input.Count - 1];
                    float previousDistance = Vector3.Dot(normal, previous - origin) + inset;
                    foreach (Vector3 current in input)
                    {
                        float distance = Vector3.Dot(normal, current - origin) + inset;
                        if ((distance <= 0f) != (previousDistance <= 0f))
                            output.Add(Vector3.LerpUnclamped(previous, current,
                                previousDistance / (previousDistance - distance)));
                        if (distance <= 0f) output.Add(current);
                        previous = current; previousDistance = distance;
                    }
                    if (output.Count == 0) return false;
                    List<Vector3> swap = input; input = output; output = swap;
                }
                return true;
            }

            public float Penetration(Vector3 point)
            {
                if (!bounds.Contains(point)) return 0f;
                float squaredDistance = float.PositiveInfinity;
                double winding = 0d;
                for (int index = 0; index < surface.Length; index += 3)
                {
                    Vector3 a = surface[index], b = surface[index + 1], c = surface[index + 2];
                    squaredDistance = Mathf.Min(squaredDistance, TriangleDistanceSquared(point, a, b, c));
                    Vector3 u = a - point, v = b - point, w = c - point;
                    double x = u.magnitude, y = v.magnitude, z = w.magnitude;
                    double denominator = x * y * z + Vector3.Dot(u, v) * z +
                        Vector3.Dot(v, w) * x + Vector3.Dot(w, u) * y;
                    winding += 2d * System.Math.Atan2(Vector3.Dot(u, Vector3.Cross(v, w)), denominator);
                }
                // Sum each closed component separately: concave bowls/shoulders
                // remain exact, and overlapping primitive solids cannot cancel
                // one another or let an internal seam hide deep penetration.
                return System.Math.Abs(winding) > System.Math.PI * 2d
                    ? Mathf.Sqrt(squaredDistance) : 0f;
            }

            private static float TriangleDistanceSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 ab = b - a, ac = c - a, ap = point - a;
                float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;
                Vector3 bp = point - b;
                float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;
                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                    return (point - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
                Vector3 cp = point - c;
                float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;
                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                    return (point - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
                float va = d3 * d6 - d5 * d4;
                if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                    return (point - (b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6)))).sqrMagnitude;
                float inverse = 1f / (va + vb + vc);
                return (point - (a + ab * (vb * inverse) + ac * (vc * inverse))).sqrMagnitude;
            }
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
