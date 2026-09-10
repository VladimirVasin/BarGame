using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class WeighbridgeAttendantTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        [Test]
        public void Cannery_ReplacesTheHumanWeighingTableauAndKeepsAFullSizePassage()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityGenerationSettings.Default, Seed);
            CityCanneryPlan site = CityCanneryPlan.Create(layout);
            Assert.That(site, Is.Not.Null);
            Assert.That(site.Descriptor.Kind, Is.EqualTo(CityDistrictPointOfInterestKind.IndustrialCannery));
            Assert.That(site.HallBounds.size.y, Is.EqualTo(4.2f).Within(.001f));
            Assert.That(WeighbridgeAttendantPlan.Create(layout).IsPresent, Is.False);
            Assert.That(CityDistrictPointOfInterestWorldBuilder.TryDescribeWeighbridgeDeck(site.Descriptor, out _), Is.False);
            RoadWalkableArea ground = RoadWalkableArea.FromLayout(layout);
            Assert.That(ground.Contains(site.World(new Vector3(-1.1f,.18f,0)),.35f), Is.True);
            Assert.That(ground.Contains(site.World(new Vector3(-5f,.18f,0)),.35f), Is.False);
        }

        [Test]
        public void Needle_DeflectionEasesUpAndSettlesBack()
        {
            // Loading: monotone rise toward full deflection.
            float deflection = 0f;
            float previous = 0f;
            for (int step = 0; step < 60; step++)
            {
                deflection =
                    CityWeighbridgeNeedleController.AdvanceDeflection(
                        deflection,
                        1f,
                        1f / 30f);
                Assert.That(deflection, Is.GreaterThan(previous));
                Assert.That(deflection, Is.LessThanOrEqualTo(1f));
                previous = deflection;
            }

            Assert.That(deflection, Is.GreaterThan(0.9f));

            // Unloading: monotone release, slower than the attack,
            // never overshooting past rest.
            float release =
                CityWeighbridgeNeedleController.AdvanceDeflection(
                    1f,
                    0f,
                    1f / 30f);
            float attack =
                CityWeighbridgeNeedleController.AdvanceDeflection(
                    0f,
                    1f,
                    1f / 30f);
            Assert.That(
                1f - release,
                Is.LessThan(attack),
                "The heavy dial releases slower than it takes load.");
            for (int step = 0; step < 240; step++)
            {
                deflection =
                    CityWeighbridgeNeedleController.AdvanceDeflection(
                        deflection,
                        0f,
                        1f / 30f);
                Assert.That(deflection, Is.GreaterThanOrEqualTo(0f));
                Assert.That(deflection, Is.LessThan(previous + 0.0001f));
                previous = deflection;
            }

            Assert.That(deflection, Is.LessThan(0.05f));
        }

        [Test]
        public void Pace_HoldsTheDeckCentreThroughTheWeighingPause()
        {
            // Before the pause the worker approaches the centre.
            Assert.That(
                WeighbridgeAttendantPresentation
                    .EvaluateCorridorProgress(0f),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                WeighbridgeAttendantPresentation
                    .EvaluateCorridorProgress(
                        WeighbridgeAttendantPresentation
                            .PauseStartNormalized * 0.5f),
                Is.EqualTo(0.25f).Within(0.0001f));

            // Through the whole authored pause he stands square at
            // the exact deck centre.
            for (float normalized =
                     WeighbridgeAttendantPresentation
                         .PauseStartNormalized;
                 normalized <=
                 WeighbridgeAttendantPresentation.PauseEndNormalized;
                 normalized += 0.02f)
            {
                Assert.That(
                    WeighbridgeAttendantPresentation
                        .EvaluateCorridorProgress(normalized),
                    Is.EqualTo(0.5f).Within(0.0001f),
                    "The weighing pause holds the deck centre.");
            }

            // After the pause he walks on to the far end.
            Assert.That(
                WeighbridgeAttendantPresentation
                    .EvaluateCorridorProgress(1f),
                Is.EqualTo(1f).Within(0.0001f));
            float resumed =
                WeighbridgeAttendantPresentation
                    .EvaluateCorridorProgress(
                        (WeighbridgeAttendantPresentation
                             .PauseEndNormalized + 1f) * 0.5f);
            Assert.That(resumed, Is.EqualTo(0.75f).Within(0.0001f));
        }

        /// <summary>
        /// The weigher holds the chalk in her right grip; the weighed
        /// worker's hands are free. Judged through the presentation's
        /// accessor and the socket in world space, so a prop attached
        /// to the wrong role (the old visibility table's failure mode)
        /// cannot pass.
        /// </summary>
        [Test]
        public void Presentation_AttachesChalkToTheWeigherOnly()
        {
            // The retired rig remains a reusable asset; this contract must
            // not recreate its former active-world tableau.
            var stances = new[] {
                new WeighbridgeAttendantStance(new CityDryingYardNpcStance(Vector3.zero,Vector3.forward),
                    WeighbridgeAttendantRole.Weigher,0,1f,0f),
                new WeighbridgeAttendantStance(new CityDryingYardNpcStance(Vector3.right*3,Vector3.forward),
                    WeighbridgeAttendantRole.WeighedWorker,2,1f,0f,Vector3.right*3+Vector3.forward*8)
            };
            WeighbridgeAttendantProvider provider =
                WeighbridgeAttendantProvider.Load();
            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.StagedPrefab, Is.Not.Null);

            var parent = new GameObject("Attendant Hand Prop Test");
            try
            {
                foreach (WeighbridgeAttendantStance stance in stances)
                {
                    GameObject instance = Object.Instantiate(
                        provider.StagedPrefab,
                        parent.transform);
                    var registry =
                        instance.GetComponent<CityPedestrianAssetRegistry>();
                    Assert.That(registry, Is.Not.Null);
                    var presentation = instance
                        .AddComponent<WeighbridgeAttendantPresentation>();
                    presentation.Initialize(registry, stance);

                    Transform socket = CityPedestrianHandProps.FindSocket(
                        registry.ModelRoot,
                        CityPedestrianHandPropId.Chalk);
                    Assert.That(socket, Is.Not.Null);
                    if (stance.Role == WeighbridgeAttendantRole.Weigher)
                    {
                        CityPedestrianHandPropRegistry chalk =
                            presentation.HeldProp;
                        Assert.That(chalk, Is.Not.Null,
                            "The weigher must hold her chalk.");
                        Assert.That(
                            chalk.Id,
                            Is.EqualTo(CityPedestrianHandPropId.Chalk));
                        Assert.That(chalk.transform.parent, Is.SameAs(socket));
                        Assert.That(
                            Vector3.Distance(
                                chalk.transform.position,
                                socket.position),
                            Is.LessThan(0.02f));
                        Assert.That(
                            chalk.FindRenderer("ACC_Chalk"),
                            Is.Not.Null);
                    }
                    else
                    {
                        Assert.That(
                            presentation.HeldProp,
                            Is.Null,
                            "The weighed worker's hands stay free.");
                        Assert.That(
                            CityPedestrianHandProps.FindAttached(
                                socket,
                                CityPedestrianHandPropId.Chalk),
                            Is.Null);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Provider_BindsTheStagedPrefabWithoutPublishingIt()
        {
            WeighbridgeAttendantProvider provider =
                WeighbridgeAttendantProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "Resources/City/WeighbridgeAttendantProvider.asset " +
                "is missing.");
            GameObject prefab = provider.StagedPrefab;
            Assert.That(
                prefab,
                Is.Not.Null,
                "The provider must reference the staged attendant " +
                "prefab.");

            var registry =
                prefab.GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo(WeighbridgeAttendantProvider.DesignId));
            Assert.That(registry.IdleClip, Is.Not.Null);
            Assert.That(
                registry.IdleClip.name,
                Is.EqualTo("WeigherCheck"));
            Assert.That(registry.WalkClip, Is.Not.Null);
            Assert.That(
                registry.WalkClip.name,
                Is.EqualTo("WeighedPace"));
            // She gained a seated loop on 2026-09-02, when she was
            // promoted to the street and onto Route 01. It belongs to the
            // bus, not to the weighbridge: the placed presentation here
            // still plays the check loop above and never touches it.
            Assert.That(registry.SitClip, Is.Not.Null);
            Assert.That(registry.SitClip.name, Is.EqualTo("WeigherSit"));
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(registry.WalkClip.isLooping, Is.True);

            // Since 2026-09-05 the chalk is a hand-prop prefab the
            // weighbridge attaches to the weigher alone; the body ships
            // empty-handed so a roaming copy (and the weighed worker)
            // holds nothing. The old skinned chalk on the FBX would be a
            // regression of the generator.
            var rendererNames = new HashSet<string>();
            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                rendererNames.Add(renderer.name);
            }

            Assert.That(
                rendererNames.Contains("ACC_Chalk"),
                Is.False,
                "The attendant body still carries 'ACC_Chalk'; the chalk " +
                "is a hand prop now.");
            Assert.That(
                CityPedestrianHandProps.FindSocket(
                    prefab.transform,
                    CityPedestrianHandPropId.Chalk),
                Is.Not.Null,
                "The chalk rides SOCKET_Grip.R.");

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            // She used to be required to stay OUT of Resources, because
            // staged and roaming were opposites. She is now both, and what
            // the weighbridge depends on is that it and the street share ONE
            // prefab asset - two copies would drift silently.
            GameObject published = Resources.Load<GameObject>(
                "Pedestrians/WeighbridgeAttendant3D");
            Assert.That(
                published,
                Is.Not.Null,
                "The attendant roams as well as standing here, so her " +
                "prefab must be loadable from Resources.");
            Assert.That(
                published,
                Is.SameAs(prefab),
                "The weighbridge and the street must share one prefab.");
            Assert.That(
                CityPedestrianResources.Roams(
                    WeighbridgeAttendantProvider.DesignId),
                Is.True);
        }

        private static float DistancePointToSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.0001f)
            {
                return Vector3.Distance(point, start);
            }

            float amount = Mathf.Clamp01(
                Vector3.Dot(point - start, segment) / lengthSquared);
            return Vector3.Distance(point, start + segment * amount);
        }

        private static CityDistrictPointOfInterestDescriptor
            FindWeighbridge(CityLayout layout)
        {
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                if (layout.DistrictPointsOfInterest[index].Kind ==
                    CityDistrictPointOfInterestKind
                        .IndustrialCannery)
                {
                    return layout.DistrictPointsOfInterest[index];
                }
            }

            Assert.Fail("The default city builds a weighbridge.");
            return default;
        }
    }
}
