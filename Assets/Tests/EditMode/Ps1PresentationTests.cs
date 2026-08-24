using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Tests.EditMode
{
    public sealed class Ps1PresentationTests
    {
        [TestCase(
            1920,
            1080,
            Ps1ResolutionPreset.Balanced640x360,
            640,
            360)]
        [TestCase(
            1920,
            1080,
            Ps1ResolutionPreset.Readable426x240,
            426,
            240)]
        [TestCase(
            1920,
            1080,
            Ps1ResolutionPreset.Authentic320x180,
            320,
            180)]
        [TestCase(
            1024,
            768,
            Ps1ResolutionPreset.Readable426x240,
            320,
            240)]
        [TestCase(
            2560,
            1080,
            Ps1ResolutionPreset.Readable426x240,
            426,
            180)]
        public void InternalResolution_PreservesAspectWithinPresetBounds(
            int outputWidth,
            int outputHeight,
            Ps1ResolutionPreset preset,
            int expectedWidth,
            int expectedHeight)
        {
            Vector2Int resolution =
                Ps1PresentationSettings.CalculateInternalResolution(
                    outputWidth,
                    outputHeight,
                    preset);

            Assert.That(resolution, Is.EqualTo(
                new Vector2Int(expectedWidth, expectedHeight)));
        }

        [Test]
        public void Resources_ContainValidProfileAndCompositeMaterial()
        {
            Ps1PresentationSettings profile =
                Resources.Load<Ps1PresentationSettings>(
                    "Rendering/Ps1PresentationProfile");
            Material material =
                Resources.Load<Material>("Materials/Ps1Composite");

            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.EffectEnabled, Is.True);
            Assert.That(
                profile.ResolutionPreset,
                Is.EqualTo(Ps1ResolutionPreset.Balanced640x360));
            Assert.That(
                profile.QuantizationStrength,
                Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(
                profile.DitherStrength,
                Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(
                profile.ScanlineIntensity,
                Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(
                profile.VertexJitterStrength,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name,
                Is.EqualTo("Hidden/BarPromenade/PS1Composite"));
            Assert.That(material.passCount, Is.EqualTo(2));
        }

        [Test]
        public void PcRenderer_ContainsConfiguredPs1CompositeFeature()
        {
            UniversalRendererData rendererData =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                    "Assets/Settings/PC_Renderer.asset");

            Assert.That(rendererData, Is.Not.Null);
            Ps1CompositeRendererFeature feature = rendererData.rendererFeatures
                .Find(item => item is Ps1CompositeRendererFeature)
                as Ps1CompositeRendererFeature;

            Assert.That(feature, Is.Not.Null);
            Assert.That(feature.PresentationSettings, Is.Not.Null);
            Assert.That(feature.CompositeMaterial, Is.Not.Null);
            Assert.That(feature.isActive, Is.True);
        }
    }
}
