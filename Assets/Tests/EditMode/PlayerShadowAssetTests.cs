using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class PlayerShadowAssetTests
    {
        [Test]
        public void ShadowCasterShader_LoadsAsOneSharedMaterial()
        {
            Shader shader = Resources.Load<Shader>(
                PlayerShadowResources.ShadowCasterShaderResourcePath);

            Assert.That(shader, Is.Not.Null);
            Assert.That(
                shader.name,
                Is.EqualTo(
                    "Bar Promenade/Player Sprite Shadow Caster"));

            Material first =
                PlayerShadowResources.ShadowCasterMaterial;
            Material second =
                PlayerShadowResources.ShadowCasterMaterial;
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.shader, Is.SameAs(shader));
            Assert.That(
                first.FindPass("ShadowCaster"),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(first.GetFloat("_Cutoff"), Is.EqualTo(0.5f));
            Assert.That(first.enableInstancing, Is.True);
        }
    }
}
