using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ScenePresentationTests
    {
        [Test]
        public void Resources_ContainNativeCompositeMaterial()
        {
            Material material = Resources.Load<Material>("Materials/SceneComposite");
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Hidden/BarPromenade/SceneComposite"));
            Assert.That(material.passCount, Is.EqualTo(2));
        }

        [Test]
        public void PcRenderer_ContainsConfiguredSceneCompositeFeature()
        {
            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/Settings/PC_Renderer.asset");
            Assert.That(renderer, Is.Not.Null);
            var feature = renderer.rendererFeatures.Find(item => item is SceneCompositeRendererFeature)
                as SceneCompositeRendererFeature;
            Assert.That(feature, Is.Not.Null);
            Assert.That(feature.CompositeMaterial, Is.Not.Null);
            Assert.That(feature.isActive, Is.True);
        }
    }
}
