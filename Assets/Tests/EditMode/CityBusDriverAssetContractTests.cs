using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBusDriverAssetContractTests
    {
        [Test]
        public void FleetDrivers_KeepDistinctPermanentDefaultNpcAssignments()
        {
            var assignments = DefaultNpcPopulation.GetAssignments();
            var ids = (
                from line in Enumerable.Range(1, CityBusDirector.MaximumLines)
                from slot in Enumerable.Range(0, CityBusDirector.VehiclesPerLine)
                select DefaultNpcPopulation.BusDriverId(line, slot)).ToArray();
            var drivers = assignments.Where(a => ids.Contains(a.CharacterId)).ToArray();
            Assert.That(drivers.Length, Is.EqualTo(ids.Length));
            Assert.That(drivers.Select(a => a.VisibleSignature).Distinct().Count(), Is.EqualTo(drivers.Length));
            var wardrobe = DefaultNpcCatalog.GetPrefab().GetComponent<NpcWardrobe>();
            var faces = DefaultNpcCatalog.GetPrefab().GetComponent<DefaultNpcAppearance>().Faces;
            foreach (var driver in drivers)
            {
                Assert.That(driver.ModelId, Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));
                Assert.That(driver.ItemIds, Does.Contain("outerwear.transit").And.Contain("headwear.transit"));
                Assert.That(driver.ItemIds.All(id => wardrobe.Items.Any(item => item.Id == id)), Is.True);
                Assert.That(faces.Single(face => face.Id == driver.FaceId).HasBlinkTile, Is.True);
                Assert.That(assignments.Count(a => a.VisibleSignature == driver.VisibleSignature), Is.EqualTo(1));
            }

            var root = new GameObject("Rematerialized driver's seat");
            try
            {
                var first = CityBusDriverRig.Create(root.transform, 3, 1);
                string faceId = first.Appearance.CurrentFaceId;
                Object.DestroyImmediate(first.gameObject);
                var second = CityBusDriverRig.Create(root.transform, 3, 1);
                Assert.That(second.Appearance.AppearanceKey, Is.EqualTo(ids.Last()));
                Assert.That(second.Appearance.CurrentFaceId, Is.EqualTo(faceId));
                Assert.That(second.Actor.IsInitialized, Is.False, "Cab pose owns the body without a street locomotion graph.");
                Assert.That(second.Hands.CylinderRadius, Is.EqualTo(.022f).Within(.00001f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
