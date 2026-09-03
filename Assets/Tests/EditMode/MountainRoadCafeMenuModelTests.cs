using BarPromenade.Runtime.World;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MountainRoadCafeMenuModelTests
    {
        [Test]
        public void FreshModel_IsHiddenWithStableOrderedItems()
        {
            var model = new MountainRoadCafeMenuModel();

            Assert.That(
                MountainRoadCafeMenuItemIds.Ordered,
                Is.EqualTo(new[]
                {
                    "mountain.cafe.menu.item.fried_eggs",
                    "mountain.cafe.menu.item.cheese_sandwich",
                    "mountain.cafe.menu.item.black_coffee"
                }));
            Assert.That(
                model.State,
                Is.EqualTo(MountainRoadCafeMenuState.Hidden));
            Assert.That(model.SelectedIndex, Is.Zero);
            Assert.That(
                model.SelectedItemId,
                Is.EqualTo(MountainRoadCafeMenuItemIds.FriedEggs));
            Assert.That(model.ConfirmedItemId, Is.Null);
        }

        [Test]
        public void MenuFocusCamera_ApproachesAlongTheViewerRayWithoutRoll()
        {
            Vector3 menuRoot = new Vector3(2f, 1f, -3f);
            Vector3 viewer = menuRoot + new Vector3(0f, 0.65f, -0.9f);
            MountainRoadCafeSeatViewPlan.EvaluateMenuCamera(
                menuRoot,
                Vector3.up,
                Vector3.forward,
                viewer,
                out Vector3 position,
                out Quaternion rotation);

            Vector3 target = menuRoot + Vector3.up *
                MountainRoadCafeSeatViewPlan.MenuSurfaceLiftMeters;
            Assert.That(
                Vector3.Distance(position, target),
                Is.EqualTo(
                    MountainRoadCafeSeatViewPlan.MenuFocusDistanceMeters)
                    .Within(0.0001f));
            Assert.That(
                Vector3.Dot(
                    rotation * Vector3.forward,
                    (target - position).normalized),
                Is.GreaterThan(0.999f));
            Assert.That(
                Vector3.Dot(rotation * Vector3.up, Vector3.up),
                Is.GreaterThan(0f));
            Assert.That(
                Vector3.Dot(
                    (position - target).normalized,
                    (viewer - target).normalized),
                Is.GreaterThan(0.999f),
                "The close-up must stay on the seated viewer's side.");
            Assert.That(
                Mathf.Abs(Vector3.Dot(
                    rotation * Vector3.right,
                    Vector3.up)),
                Is.LessThan(0.001f),
                "The close-up must not inherit page roll.");
            Assert.That(position.y, Is.GreaterThan(target.y));
            Assert.That(
                Vector3.Distance(position, target),
                Is.LessThan(Vector3.Distance(viewer, target)));
            Assert.That(
                MountainRoadCafeSeatViewPlan.MenuFocusFieldOfView,
                Is.LessThan(MountainRoadCafeSeatViewPlan.FieldOfView));
        }

        [Test]
        public void MenuFocusCamera_CorrectsAnImportedUndersideNormal()
        {
            Vector3 menuRoot = new Vector3(-1f, 1.1f, 4f);
            Vector3 viewer = menuRoot + new Vector3(0f, 0.7f, -0.8f);

            MountainRoadCafeSeatViewPlan.EvaluateMenuCamera(
                menuRoot,
                Vector3.down,
                Vector3.forward,
                viewer,
                out Vector3 position,
                out Quaternion rotation);

            Assert.That(position.y, Is.GreaterThan(menuRoot.y));
            Assert.That(
                Vector3.Dot(rotation * Vector3.up, Vector3.up),
                Is.GreaterThan(0f));
        }

        [Test]
        public void DeliveryLifecycle_AdvancesOnlyInOrder()
        {
            var model = new MountainRoadCafeMenuModel();

            Assert.That(model.Open(), Is.False);
            Assert.That(model.BeginDelivery(), Is.True);
            Assert.That(
                model.State,
                Is.EqualTo(MountainRoadCafeMenuState.Delivering));
            Assert.That(model.BeginDelivery(), Is.False);
            Assert.That(model.Confirm(), Is.False);

            Assert.That(model.Open(), Is.True);
            Assert.That(
                model.State,
                Is.EqualTo(MountainRoadCafeMenuState.Open));
            Assert.That(model.Open(), Is.False);
        }

        [Test]
        public void OpenNavigation_WrapsInBothDirections()
        {
            MountainRoadCafeMenuModel model = CreateOpenModel();

            Assert.That(model.MovePrevious(), Is.True);
            Assert.That(
                model.SelectedItemId,
                Is.EqualTo(MountainRoadCafeMenuItemIds.BlackCoffee));
            Assert.That(model.MoveNext(), Is.True);
            Assert.That(
                model.SelectedItemId,
                Is.EqualTo(MountainRoadCafeMenuItemIds.FriedEggs));
            Assert.That(model.MoveNext(), Is.True);
            Assert.That(
                model.SelectedItemId,
                Is.EqualTo(MountainRoadCafeMenuItemIds.CheeseSandwich));
        }

        [Test]
        public void Navigation_RequiresOpenState()
        {
            var model = new MountainRoadCafeMenuModel();

            Assert.That(model.MovePrevious(), Is.False);
            Assert.That(model.MoveNext(), Is.False);
            model.BeginDelivery();
            Assert.That(model.MovePrevious(), Is.False);
            Assert.That(model.MoveNext(), Is.False);
            Assert.That(model.SelectedIndex, Is.Zero);
        }

        [Test]
        public void Confirm_CommitsSelectionOnceAndThenIsIdempotent()
        {
            MountainRoadCafeMenuModel model = CreateOpenModel();
            model.MoveNext();

            Assert.That(model.Confirm(), Is.True);
            Assert.That(
                model.State,
                Is.EqualTo(MountainRoadCafeMenuState.Confirmed));
            Assert.That(
                model.ConfirmedItemId,
                Is.EqualTo(MountainRoadCafeMenuItemIds.CheeseSandwich));

            Assert.That(model.MoveNext(), Is.False);
            Assert.That(model.MovePrevious(), Is.False);
            Assert.That(model.Confirm(), Is.False);
            Assert.That(
                model.ConfirmedItemId,
                Is.EqualTo(MountainRoadCafeMenuItemIds.CheeseSandwich));

            Assert.That(model.BeginRetrieval(), Is.True);
            Assert.That(
                model.State,
                Is.EqualTo(MountainRoadCafeMenuState.Retrieving));
            Assert.That(model.CompleteRetrieval(), Is.True);
            Assert.That(
                model.State,
                Is.EqualTo(MountainRoadCafeMenuState.Closed));
            Assert.That(
                model.ConfirmedItemId,
                Is.EqualTo(MountainRoadCafeMenuItemIds.CheeseSandwich));
        }

        [Test]
        public void RetrievalWithoutConfirmation_ClosesWithoutCommittedItem()
        {
            MountainRoadCafeMenuModel model = CreateOpenModel();

            Assert.That(model.BeginRetrieval(), Is.True);
            Assert.That(model.BeginRetrieval(), Is.False);
            Assert.That(model.Confirm(), Is.False);
            Assert.That(model.CompleteRetrieval(), Is.True);

            Assert.That(
                model.State,
                Is.EqualTo(MountainRoadCafeMenuState.Closed));
            Assert.That(model.ConfirmedItemId, Is.Null);
            Assert.That(model.CompleteRetrieval(), Is.False);
        }

        [Test]
        public void Reset_ClearsCommitAndRestoresInitialSelection()
        {
            MountainRoadCafeMenuModel model = CreateOpenModel();
            model.MovePrevious();
            model.Confirm();

            model.Reset();

            Assert.That(
                model.State,
                Is.EqualTo(MountainRoadCafeMenuState.Hidden));
            Assert.That(model.SelectedIndex, Is.Zero);
            Assert.That(
                model.SelectedItemId,
                Is.EqualTo(MountainRoadCafeMenuItemIds.FriedEggs));
            Assert.That(model.ConfirmedItemId, Is.Null);
            Assert.That(model.BeginDelivery(), Is.True);
        }

        private static MountainRoadCafeMenuModel CreateOpenModel()
        {
            var model = new MountainRoadCafeMenuModel();
            Assert.That(model.BeginDelivery(), Is.True);
            Assert.That(model.Open(), Is.True);
            return model;
        }
    }
}
