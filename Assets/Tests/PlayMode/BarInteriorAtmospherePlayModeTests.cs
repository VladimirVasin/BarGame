using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class BarInteriorAtmospherePlayModeTests
    {
        private GameObject atmosphereObject;

        [UnityTest]
        public IEnumerator InitializedDust_UsesMatchingVelocityCurveModes()
        {
            atmosphereObject =
                new GameObject("Bar Atmosphere Test");
            BarInteriorAtmosphere atmosphere =
                atmosphereObject.AddComponent<BarInteriorAtmosphere>();

            atmosphere.Initialize(
                Array.Empty<BarPracticalLightSpec>());
            yield return null;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                atmosphere.Dust.velocityOverLifetime;
            Assert.That(velocity.enabled, Is.True);
            Assert.That(
                velocity.x.mode,
                Is.EqualTo(ParticleSystemCurveMode.TwoConstants));
            Assert.That(velocity.y.mode, Is.EqualTo(velocity.x.mode));
            Assert.That(velocity.z.mode, Is.EqualTo(velocity.x.mode));
            Assert.That(velocity.z.constantMin, Is.Zero);
            Assert.That(velocity.z.constantMax, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (atmosphereObject != null)
            {
                UnityEngine.Object.Destroy(atmosphereObject);
            }

            yield return null;
        }
    }
}
