using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class DefaultNpcWomanAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type setup = Type.GetType("BarPromenade.Editor.DefaultNpcWomanAssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class DefaultNpcFactoryPlayModeTests
    {
        [UnityTest]
        [PrebuildSetup(typeof(DefaultNpcWomanAssetsSetup))]
        public IEnumerator OrdinaryWomen_KeepVisibleAssignmentsAndRideTheBus()
        {
            var population = DefaultNpcPopulation.GetAssignments();
            var women = population.Where(item => item.ModelId == DefaultNpcCatalog.OrdinaryWoman).ToArray();
            Assert.That(women, Has.Length.EqualTo(48));
            Assert.That(women.All(item => item.CharacterId.StartsWith(DefaultNpcPopulation.PedestrianIdPrefix, StringComparison.Ordinal) &&
                item.HeightCm >= 155 && item.HeightCm <= 178));
            Assert.That(women.Select(item => item.VisibleSignature).Distinct().Count(), Is.EqualTo(women.Length));
            Assert.That(women.Select(item => item.FaceId).Distinct().Count(), Is.EqualTo(4));
            var bareHeaded = women.Where(item => !item.ItemIds.Contains("headwear.everyday")).ToArray();
            CollectionAssert.AreEquivalent(new[] { "hair.loose", "hair.ponytail", "hair.braid" },
                bareHeaded.Select(item => item.ItemIds.Single(id => id.StartsWith("hair.", StringComparison.Ordinal))).Distinct());
            Assert.That(bareHeaded.Select(item => item.HairColorId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(women.All(item => item.VisibleSignature.Contains("|hair.")));
            Assert.That(DefaultNpcPopulation.GetAssignment(DefaultNpcPopulation.PedestrianId(0)).ModelId,
                Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));
            Assert.That(DefaultNpcPopulation.GetAssignment(DefaultNpcPopulation.FairVisitorId(0)).ModelId,
                Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));

            host = new GameObject("Default woman regression");
            foreach (var assignment in new[] { bareHeaded.First(), women.Except(bareHeaded).First() })
            {
                var resident = DefaultNpcFactory.CreateForCharacter(host.transform, assignment.CharacterId);
                var wardrobe = resident.GetComponent<NpcWardrobe>();
                var appearance = resident.GetComponent<DefaultNpcAppearance>();
                var body = resident.GetComponent<DefaultNpcBodyShape>();
                Assert.That(resident.GetComponent<CanneryWomanPresentation>(), Is.Null);
                Assert.That(appearance.CurrentAtlas, Is.Not.SameAs(wardrobe.Atlas));
                resident.gameObject.SetActive(false);
                yield return null;
                resident.gameObject.SetActive(true);
                var recreated = DefaultNpcFactory.CreateForCharacter(host.transform, assignment.CharacterId);
                CollectionAssert.AreEqual(wardrobe.EquippedItemIds, recreated.GetComponent<NpcWardrobe>().EquippedItemIds);
                CollectionAssert.AreEqual(assignment.ItemIds, wardrobe.EquippedItemIds);
                Assert.That(appearance.AppearanceKey, Is.EqualTo(assignment.CharacterId));
                Assert.That(appearance.CurrentFaceId, Is.EqualTo(assignment.FaceId));
                Assert.That(appearance.CurrentHairColorId, Is.EqualTo(assignment.HairColorId));
                Assert.That(body.HeightCm, Is.EqualTo(assignment.HeightCm));
                Assert.That(body.Build, Is.EqualTo(assignment.Build));
                var visibleHair = wardrobe.Items.Where(item => item.Slot == NpcWardrobe.HairStyleSlot)
                    .Where(item => item.Parts.Any(part => part.Renderer.enabled)).ToArray();
                Assert.That(visibleHair, Has.Length.EqualTo(1));
                Assert.That(visibleHair[0].Parts.All(part => part.Renderer.enabled));
                var speaker = new NpcSpeaker(resident, resident.Head, assignment.CharacterId, NpcEarshotProfile.Conversation,
                    NpcVoiceCatalog.ResolveGender(assignment.ModelId));
                Assert.That(speaker.Gender, Is.EqualTo(RussianVocalizerGender.Female));
            }

            // The tallest and shortest assigned silhouettes board at the front,
            // use the sprung seat, then walk through the rear doorway again.
            foreach (var assignment in new[] { women.OrderBy(item => item.HeightCm).First(), women.OrderByDescending(item => item.HeightCm).First() })
            {
                int index = int.Parse(assignment.CharacterId.Substring(DefaultNpcPopulation.PedestrianIdPrefix.Length),
                    System.Globalization.CultureInfo.InvariantCulture);
                CityBusNpcPassengerPlayModeTests.CheckPassengerFeet(index, true);
            }
        }
    }
}
