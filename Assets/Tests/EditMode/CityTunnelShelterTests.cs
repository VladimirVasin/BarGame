using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityTunnelShelterTests
    {
        [Test]
        [Category("CityMountain")]
        public void OpenTunnel_SheltersOnlyPastThePortalAndWithinTheLining()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                58021);
            CityMountainTunnelDescriptor tunnel =
                CityMountainBoundaryPlanner.Create(layout).Tunnel;
            Vector3 right = Vector3.Cross(
                Vector3.up,
                tunnel.Axis).normalized;

            Assert.That(
                CityTunnelShelterController.Contains(
                    tunnel,
                    tunnel.PortalGroundCenter - tunnel.Axis,
                    false),
                Is.False,
                "The forecourt must retain exterior weather.");
            Assert.That(
                CityTunnelShelterController.Contains(
                    tunnel,
                    tunnel.PortalGroundCenter +
                    tunnel.Axis *
                    (CityTunnelShelterController.ShelterEntryInset + 0.1f),
                    false),
                Is.True,
                "The physical lining must shelter the player.");
            Assert.That(
                CityTunnelShelterController.Contains(
                    tunnel,
                    tunnel.PortalGroundCenter +
                    tunnel.Axis *
                    (CityTunnelShelterController.ShelterExitInset + 0.05f),
                    true),
                Is.True,
                "Exit hysteresis must not pulse weather at the mouth.");
            Assert.That(
                CityTunnelShelterController.Contains(
                    tunnel,
                    tunnel.PortalGroundCenter +
                    tunnel.Axis +
                    right * (tunnel.OpeningWidth + 1f),
                    false),
                Is.False,
                "The shelter volume must not spill sideways through rock.");
        }
    }
}
