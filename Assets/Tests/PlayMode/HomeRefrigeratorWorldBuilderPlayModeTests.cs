using System.Collections;
using System.Collections.ObjectModel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeRefrigeratorWorldBuilderPlayModeTests
    {
        private GameObject owner;

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
        public IEnumerator Build_CreatesAnimatedHollowStorageWithoutLights()
        {
            owner = new GameObject("Refrigerator Builder Test Owner");
            HomeRefrigeratorPlan plan =
                HomeRefrigeratorPlan.Create(
                    HomeInteriorLayoutPlanner.Generate());
            HomeRefrigeratorView view =
                HomeRefrigeratorWorldBuilder.Build(
                    owner.transform,
                    plan);

            // RuntimePrimitiveFactory removes colliderless primitive colliders
            // at the end of the frame in Play Mode.
            yield return null;

            Assert.That(view, Is.Not.Null);
            Assert.That(
                view.transform.localPosition,
                Is.EqualTo(plan.RootPosition));
            Assert.That(view.SlotRoots, Has.Count.EqualTo(8));
            Assert.That(
                view.GetComponentsInChildren<Light>(true),
                Is.Empty,
                "The refrigerator glow must not add another real light.");

            Collider[] colliders =
                view.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders, Has.Length.EqualTo(4));
            Assert.That(colliders, Does.Contain(view.BodyCollider));
            Assert.That(view.BodyCollider.enabled, Is.True);
            Assert.That(view.Items, Has.Count.EqualTo(3));
            Assert.That(
                view.Items,
                Is.InstanceOf<
                    ReadOnlyCollection<HomeRefrigeratorItemView>>());

            for (int index = 0; index < plan.Slots.Count; index++)
            {
                HomeRefrigeratorSlotPlan slot = plan.Slots[index];
                Assert.That(
                    view.TryGetSlot(slot.Id, out Transform slotRoot),
                    Is.True,
                    slot.Id);
                Assert.That(
                    slotRoot.localPosition,
                    Is.EqualTo(slot.LocalPosition),
                    slot.Id);
                Assert.That(
                    slotRoot.parent,
                    Is.SameAs(
                        slot.Parent == HomeRefrigeratorSlotParent.Cavity
                            ? view.InteriorRoot
                            : view.DoorPivot),
                    slot.Id);
                HomeRefrigeratorSlotView marker =
                    slotRoot.GetComponent<HomeRefrigeratorSlotView>();
                Assert.That(marker, Is.Not.Null, slot.Id);
                Assert.That(marker.SlotId, Is.EqualTo(slot.Id));
                Assert.That(marker.Size, Is.EqualTo(slot.Size));
                Assert.That(
                    marker.InitialOccupant,
                    Is.EqualTo(slot.Occupant));

                if (!slot.IsOccupied)
                {
                    continue;
                }

                Assert.That(
                    view.TryGetItem(
                        slot.Occupant,
                        out HomeRefrigeratorItemView item),
                    Is.True,
                    slot.Id);
                Assert.That(item.Kind, Is.EqualTo(slot.Occupant));
                Assert.That(item.SlotId, Is.EqualTo(slot.Id));
                Assert.That(item.OriginalRoot, Is.SameAs(item.transform));
                Assert.That(
                    item.OriginalRoot.parent,
                    Is.SameAs(slotRoot));
                Assert.That(item.Renderers, Is.Not.Empty);
                Assert.That(
                    item.Renderers,
                    Is.InstanceOf<ReadOnlyCollection<Renderer>>());
                Assert.That(item.SelectionCollider, Is.Not.Null);
                Assert.That(item.SelectionCollider.enabled, Is.True);
                Assert.That(item.SelectionCollider.isTrigger, Is.True);
                Assert.That(
                    item.SelectionCollider.transform,
                    Is.SameAs(item.OriginalRoot));
                Assert.That(
                    item.SelectionCollider,
                    Is.TypeOf<BoxCollider>());
                AssertSelectionBounds(item);
            }

            Assert.That(
                view.TryGetItem(
                    HomeRefrigeratorItemKind.None,
                    out _),
                Is.False);

            AssertRequiredChild(view.transform, "Home Refrigerator Shelf 1");
            AssertRequiredChild(view.transform, "Home Refrigerator Shelf 2");
            AssertRequiredChild(view.transform, "Home Refrigerator Shelf 3");
            Transform cabinetBack = AssertRequiredChild(
                view.transform,
                "Home Refrigerator Cabinet Back");
            Transform cavityBack = AssertRequiredChild(
                view.transform,
                "Home Refrigerator Cavity Back Liner");
            Assert.That(
                cavityBack.GetComponent<Renderer>().bounds.max.z,
                Is.LessThanOrEqualTo(
                    cabinetBack.GetComponent<Renderer>().bounds.min.z +
                    0.001f),
                "The structural back must not fill the visible cavity.");

            AssertRequiredChild(
                view.transform,
                "Home Refrigerator Lower Drawer Front");
            AssertRequiredChild(
                view.transform,
                "Home Refrigerator Door Bin 1 Front Rail");
            AssertRequiredChild(
                view.transform,
                "Home Refrigerator Door Bin 2 Front Rail");
            Transform vodka = AssertRequiredChild(
                view.transform,
                "Home Refrigerator Vodka Bottle");
            Transform egg = AssertRequiredChild(
                view.transform,
                "Home Refrigerator Chicken Egg");
            Transform stew = AssertRequiredChild(
                view.transform,
                "Home Refrigerator Open Stew Can");

            Renderer[] bottleRenderers =
                vodka.GetComponentsInChildren<Renderer>(true);
            for (int index = 0;
                 index < bottleRenderers.Length;
                 index++)
            {
                Assert.That(
                    view.InteriorLightStrip.bounds.Intersects(
                        bottleRenderers[index].bounds),
                    Is.False,
                    bottleRenderers[index].name);
            }

            Renderer[] renderers =
                view.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            for (int index = 0; index < renderers.Length; index++)
            {
                Assert.That(renderers[index].sharedMaterial, Is.Not.Null);
                Assert.That(
                    renderers[index].sharedMaterial.name,
                    Does.Not.Contain("(Instance)"));
            }

            Quaternion closedDoor = view.DoorPivot.localRotation;
            Quaternion closedHandle = view.HandlePivot.localRotation;
            view.ApplyPresentation(1.011f, 1f, 1f);
            Assert.That(
                Quaternion.Angle(
                    closedDoor,
                    view.DoorPivot.localRotation),
                Is.EqualTo(plan.DoorOpenAngle * 1.011f).Within(0.1f));
            Assert.That(
                Quaternion.Angle(
                    closedHandle,
                    view.HandlePivot.localRotation),
                Is.GreaterThan(10f));
            Assert.That(view.InteriorLightStrip.enabled, Is.True);
            Assert.That(view.InteriorHalo.IsVisible, Is.True);

            Camera inspectionCamera =
                new GameObject("Refrigerator Inspection Test Camera")
                    .AddComponent<Camera>();
            inspectionCamera.transform.SetPositionAndRotation(
                plan.CameraPosition,
                Quaternion.LookRotation(
                    plan.CameraLookAt - plan.CameraPosition,
                    Vector3.up));
            inspectionCamera.fieldOfView = plan.CameraFieldOfView;
            inspectionCamera.aspect = 16f / 9f;
            AssertFullyInsideInspectionFrame(
                inspectionCamera,
                vodka,
                "vodka bottle");
            AssertFullyInsideInspectionFrame(
                inspectionCamera,
                egg,
                "chicken egg");
            AssertFullyInsideInspectionFrame(
                inspectionCamera,
                stew,
                "open stew can");
            Object.Destroy(inspectionCamera.gameObject);

            view.ResetPresentation();
            Assert.That(
                Quaternion.Angle(
                    closedDoor,
                    view.DoorPivot.localRotation),
                Is.LessThan(0.01f));
            Assert.That(view.InteriorLightStrip.enabled, Is.False);
            Assert.That(view.InteriorHalo.IsVisible, Is.False);
        }

        private static Transform AssertRequiredChild(
            Transform root,
            string name)
        {
            Transform child = FindChild(root, name);
            Assert.That(child, Is.Not.Null, name);
            return child;
        }

        private static Transform FindChild(
            Transform parent,
            string name)
        {
            if (parent.name == name)
            {
                return parent;
            }

            for (int index = 0; index < parent.childCount; index++)
            {
                Transform found = FindChild(parent.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void AssertFullyInsideInspectionFrame(
            Camera camera,
            Transform item,
            string description)
        {
            Renderer[] renderers =
                item.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty, description);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        var corner = new Vector3(
                            x == 0 ? bounds.min.x : bounds.max.x,
                            y == 0 ? bounds.min.y : bounds.max.y,
                            z == 0 ? bounds.min.z : bounds.max.z);
                        Vector3 viewport =
                            camera.WorldToViewportPoint(corner);
                        Assert.That(
                            viewport.z,
                            Is.GreaterThan(0f),
                            description);
                        Assert.That(
                            viewport.x,
                            Is.InRange(0.02f, 0.98f),
                            description);
                        Assert.That(
                            viewport.y,
                            Is.InRange(0.02f, 0.98f),
                            description);
                    }
                }
            }
        }

        private static void AssertSelectionBounds(
            HomeRefrigeratorItemView item)
        {
            Bounds visualBounds = item.Renderers[0].bounds;
            for (int index = 1; index < item.Renderers.Count; index++)
            {
                visualBounds.Encapsulate(item.Renderers[index].bounds);
            }

            Bounds selectionBounds = item.SelectionCollider.bounds;
            Assert.That(
                Vector3.Distance(selectionBounds.center, visualBounds.center),
                Is.LessThan(0.001f),
                item.Kind.ToString());
            Assert.That(
                Vector3.Distance(selectionBounds.size, visualBounds.size),
                Is.LessThan(0.001f),
                item.Kind.ToString());

            for (int index = 0; index < item.Renderers.Count; index++)
            {
                Bounds rendererBounds = item.Renderers[index].bounds;
                Assert.That(
                    ContainsWithTolerance(
                        selectionBounds,
                        rendererBounds.min),
                    Is.True,
                    item.Renderers[index].name);
                Assert.That(
                    ContainsWithTolerance(
                        selectionBounds,
                        rendererBounds.max),
                    Is.True,
                    item.Renderers[index].name);
            }
        }

        private static bool ContainsWithTolerance(
            Bounds bounds,
            Vector3 point)
        {
            const float tolerance = 0.0001f;
            return point.x >= bounds.min.x - tolerance &&
                   point.x <= bounds.max.x + tolerance &&
                   point.y >= bounds.min.y - tolerance &&
                   point.y <= bounds.max.y + tolerance &&
                   point.z >= bounds.min.z - tolerance &&
                   point.z <= bounds.max.z + tolerance;
        }
    }
}
