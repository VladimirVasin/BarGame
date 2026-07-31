using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeRefrigeratorItemCatalogTests
    {
        [Test]
        public void All_CoversEveryConcreteItemExactlyOnce()
        {
            HomeRefrigeratorItemKind[] concreteKinds =
                Enum.GetValues(typeof(HomeRefrigeratorItemKind))
                    .Cast<HomeRefrigeratorItemKind>()
                    .Where(kind => kind != HomeRefrigeratorItemKind.None)
                    .ToArray();

            Assert.That(
                HomeRefrigeratorItemCatalog.All,
                Has.Count.EqualTo(concreteKinds.Length));
            Assert.That(
                HomeRefrigeratorItemCatalog.All
                    .Select(definition => definition.Kind),
                Is.EquivalentTo(concreteKinds));
            Assert.That(
                HomeRefrigeratorItemCatalog.All
                    .Select(definition => definition.Kind)
                    .Distinct()
                    .Count(),
                Is.EqualTo(concreteKinds.Length));
            Assert.That(
                HomeRefrigeratorItemCatalog.All,
                Is.InstanceOf<ReadOnlyCollection<
                    HomeRefrigeratorItemDefinition>>());

            IList<HomeRefrigeratorItemDefinition> mutableView =
                (IList<HomeRefrigeratorItemDefinition>)
                    HomeRefrigeratorItemCatalog.All;
            Assert.That(
                () => mutableView.Add(default),
                Throws.TypeOf<NotSupportedException>());
        }

        [TestCase(
            HomeRefrigeratorItemKind.VodkaBottle,
            "home.refrigerator.item.vodka.name",
            "home.refrigerator.item.vodka.description")]
        [TestCase(
            HomeRefrigeratorItemKind.ChickenEgg,
            "home.refrigerator.item.egg.name",
            "home.refrigerator.item.egg.description")]
        [TestCase(
            HomeRefrigeratorItemKind.OpenStewCan,
            "home.refrigerator.item.stew_can.name",
            "home.refrigerator.item.stew_can.description")]
        public void Definition_ProvidesStableKeysAndFinitePreviewPose(
            HomeRefrigeratorItemKind kind,
            string expectedNameKey,
            string expectedDescriptionKey)
        {
            Assert.That(
                HomeRefrigeratorItemCatalog.TryGet(
                    kind,
                    out HomeRefrigeratorItemDefinition definition),
                Is.True);
            Assert.That(definition.Kind, Is.EqualTo(kind));
            Assert.That(
                definition.NameLocalizationKey,
                Is.EqualTo(expectedNameKey));
            Assert.That(
                definition.DescriptionLocalizationKey,
                Is.EqualTo(expectedDescriptionKey));
            Assert.That(
                definition.PreviewScale,
                Is.InRange(0.5f, 3f));
            Assert.That(
                IsFinite(definition.PreviewLocalRotation.x) &&
                IsFinite(definition.PreviewLocalRotation.y) &&
                IsFinite(definition.PreviewLocalRotation.z) &&
                IsFinite(definition.PreviewLocalRotation.w),
                Is.True);
            Assert.That(
                UnityEngine.Quaternion.Dot(
                    definition.PreviewLocalRotation,
                    definition.PreviewLocalRotation),
                Is.EqualTo(1f).Within(0.0001f));

            HomeRefrigeratorItemDefinition required =
                HomeRefrigeratorItemCatalog.Get(kind);
            Assert.That(required.Kind, Is.EqualTo(kind));
            Assert.That(
                required.PreviewLocalRotation,
                Is.EqualTo(definition.PreviewLocalRotation));
            Assert.That(
                required.PreviewScale,
                Is.EqualTo(definition.PreviewScale));
        }

        [Test]
        public void Lookup_RejectsNoneAndUnknownKinds()
        {
            Assert.That(
                HomeRefrigeratorItemCatalog.TryGet(
                    HomeRefrigeratorItemKind.None,
                    out _),
                Is.False);
            Assert.That(
                () => HomeRefrigeratorItemCatalog.Get(
                    HomeRefrigeratorItemKind.None),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => HomeRefrigeratorItemCatalog.Get(
                    (HomeRefrigeratorItemKind)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
