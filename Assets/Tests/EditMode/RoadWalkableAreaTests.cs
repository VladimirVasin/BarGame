using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class RoadWalkableAreaTests
    {
        [Test]
        public void Contains_UsesUnionAndAccountsForPlayerRadius()
        {
            var area = new RoadWalkableArea(
                new[]
                {
                    new Rect(0f, 0f, 10f, 4f),
                    new Rect(4f, 0f, 2f, 10f)
                });

            Assert.That(area.Contains(new Vector3(8f, 0f, 2f), 0.5f), Is.True);
            Assert.That(area.Contains(new Vector3(5f, 0f, 8f), 0.5f), Is.True);
            Assert.That(area.Contains(new Vector3(0.25f, 0f, 2f), 0.5f), Is.False);
            Assert.That(area.Contains(new Vector3(9.5f, 0f, 3.5f), 0.5f), Is.True);
        }

        [Test]
        public void Constrain_ReturnsDesiredPositionWhenItIsWalkable()
        {
            var area = new RoadWalkableArea(
                new[] { new Rect(0f, 0f, 10f, 4f) });
            var desired = new Vector3(6f, 1.2f, 2f);

            Vector3 constrained = area.Constrain(
                new Vector3(2f, 0f, 2f),
                desired,
                0.5f);

            Assert.That(constrained, Is.EqualTo(desired));
        }

        [Test]
        public void Constrain_FallsBackToSingleValidAxis()
        {
            var area = new RoadWalkableArea(
                new[] { new Rect(0f, 0f, 10f, 4f) });
            var current = new Vector3(2f, 0f, 2f);

            Vector3 constrained = area.Constrain(
                current,
                new Vector3(6f, 0f, 7f),
                0.5f);

            Assert.That(constrained, Is.EqualTo(new Vector3(6f, 0f, 2f)));
        }

        [Test]
        public void Constrain_StaysPutWhenNeitherAxisIsValid()
        {
            var area = new RoadWalkableArea(
                new[] { new Rect(0f, 0f, 4f, 4f) });
            var current = new Vector3(2f, 0f, 2f);

            Vector3 constrained = area.Constrain(
                current,
                new Vector3(8f, 0f, 8f),
                0.5f);

            Assert.That(constrained, Is.EqualTo(current));
        }
    }
}
