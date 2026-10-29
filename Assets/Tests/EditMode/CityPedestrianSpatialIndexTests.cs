using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityPedestrianSpatialIndexTests
    {
        [Test]
        public void Query_CrossesNegativeCellBoundariesInStableSlotOrder()
        {
            var index = new CityPedestrianSpatialIndex();
            index.BeginRebuild(6);
            index.Update(5, new Vector3(.1f, 10f, .1f));
            index.Update(3, new Vector3(-.1f, -10f, .1f));
            index.Update(1, new Vector3(.1f, 0f, -.1f));
            index.Update(4, new Vector3(-.1f, 0f, -.1f));
            index.Update(0, new Vector3(1.9f, 0f, 0f));
            index.Update(2, Vector3.one, false);

            Assert.That(Query(index, Vector3.zero, .3f),
                Is.EqualTo(new[] { 1, 3, 4, 5 }),
                "Height and cell enumeration order must not change planar peer priority.");
        }

        [Test]
        public void MovingSlots_MatchOrderedPeerScanAfterEveryUpdate()
        {
            const int count = 48;
            var positions = new Vector3[count];
            var present = new bool[count];
            var index = new CityPedestrianSpatialIndex();
            index.BeginRebuild(count);
            for (int slot = count - 1; slot >= 0; slot--)
            {
                positions[slot] = new Vector3(
                    (slot % 8 - 4) * .33f, 0f, (slot / 8 - 3) * .33f);
                present[slot] = true;
                index.Update(slot, positions[slot]);
            }

            const float radius = CityPedestrianDirector.PedestrianAvoidanceDistance;
            var expected = new List<int>();
            for (int moved = 0; moved < count; moved++)
            {
                // Cross several cells in one step, and remove bodies that
                // have entered a vehicle or returned their presentation.
                positions[moved] += new Vector3(-4.2f, 0f, 3.9f);
                present[moved] = moved % 4 != 0;
                index.Update(moved, positions[moved], present[moved]);
                for (int observer = 0; observer < count; observer += 7)
                {
                    expected.Clear();
                    for (int peer = 0; peer < count; peer++)
                    {
                        Vector3 offset = positions[peer] - positions[observer];
                        if (present[peer] && offset.x * offset.x + offset.z * offset.z <= radius * radius)
                            expected.Add(peer);
                    }

                    Assert.That(Query(index, positions[observer], radius),
                        Is.EqualTo(expected),
                        $"Peer set changed after moving slot {moved}, observer {observer}.");
                }
            }
        }

        [Test]
        public void Rebuild_GrowsForDensePopulationAndDropsAllPreviousEntries()
        {
            var index = new CityPedestrianSpatialIndex();
            index.BeginRebuild(2);
            index.Update(0, Vector3.zero);
            index.Update(1, Vector3.zero);
            index.BeginRebuild(80);
            for (int slot = 79; slot >= 0; slot--) index.Update(slot, Vector3.zero);
            Assert.That(index.Query(Vector3.zero, .78f), Is.EqualTo(80),
                "A dense cell must return every peer instead of silently overflowing a buffer.");
            for (int slot = 0; slot < 80; slot++)
                Assert.That(index.GetActorIndex(slot), Is.EqualTo(slot));

            index.BeginRebuild(2);
            index.Update(1, new Vector3(10f, 0f, -10f));
            Assert.That(index.Query(Vector3.zero, .78f), Is.Zero);
            Assert.That(Query(index, new Vector3(10f, 0f, -10f), .78f),
                Is.EqualTo(new[] { 1 }));
        }

        private static int[] Query(CityPedestrianSpatialIndex index, Vector3 position, float radius)
        {
            int count = index.Query(position, radius);
            var result = new int[count];
            for (int entry = 0; entry < count; entry++) result[entry] = index.GetActorIndex(entry);
            return result;
        }
    }
}
