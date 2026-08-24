using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class AreaTravelContractTests
    {
        [TestCase(GameAreaId.City, SceneIds.City)]
        [TestCase(GameAreaId.MountainRoad, SceneIds.MountainRoad)]
        public void SceneCatalog_MapsEachAreaToOneSeparateScene(
            GameAreaId area,
            string expectedScene)
        {
            Assert.That(
                AreaSceneCatalog.IsSupported(area),
                Is.True);
            Assert.That(
                AreaSceneCatalog.GetSceneName(area),
                Is.EqualTo(expectedScene));
            Assert.That(
                AreaSceneCatalog.TryGetArea(
                    expectedScene,
                    out GameAreaId roundTrip),
                Is.True);
            Assert.That(roundTrip, Is.EqualTo(area));
            Assert.That(
                expectedScene,
                Is.Not.EqualTo(SceneIds.AreaLoading));
        }

        [Test]
        public void Request_PreservesDestinationAndArrivalSemantics()
        {
            var request = new AreaTravelRequest(
                GameAreaId.MountainRoad,
                AreaArrivalToken.MapTeleport);

            Assert.That(request.IsValid, Is.True);
            Assert.That(
                request.DestinationArea,
                Is.EqualTo(GameAreaId.MountainRoad));
            Assert.That(
                request.ArrivalToken,
                Is.EqualTo(AreaArrivalToken.MapTeleport));
            Assert.That(
                request,
                Is.EqualTo(
                    new AreaTravelRequest(
                        GameAreaId.MountainRoad,
                        AreaArrivalToken.MapTeleport)));
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(0.45f, 0.275f, 0.5f)]
        [TestCase(0.90f, 0.275f, 0.5f)]
        [TestCase(0.45f, 1f, 0.5f)]
        [TestCase(0.90f, 1f, 1f)]
        public void LoadingProgress_WaitsForBothLoadAndReadableHold(
            float sceneProgress,
            float visibleSeconds,
            float expected)
        {
            Assert.That(
                AreaTravelService.EvaluateDisplayedProgress(
                    sceneProgress,
                    visibleSeconds),
                Is.EqualTo(expected).Within(0.0001f));
        }
    }
}
