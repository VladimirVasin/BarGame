using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarBartenderAssetTests
    {
        private const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";

        [Test]
        public void Provider_SelectsOrdinaryPrefab_AndRetainsLegacy()
        {
            BarBartenderProvider provider = BarBartenderProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "The bartender provider asset must be addressable.");
            Assert.That(provider.BartenderPrefab, Is.Not.Null);
            Assert.That(provider.LegacyBartenderPrefab, Is.Not.Null);
            Assert.That(
                provider.BartenderPrefab,
                Is.Not.SameAs(provider.LegacyBartenderPrefab));

            BarBartenderAssetRegistry active =
                provider.BartenderPrefab.GetComponent<
                    BarBartenderAssetRegistry>();
            Assert.That(active, Is.Not.Null);
            Assert.That(active.Animator, Is.Not.Null);
            Assert.That(active.Animator.avatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(active.Animator.avatar),
                Is.EqualTo(PlayerModelPath));
            Assert.That(
                active.DesignId,
                Is.EqualTo(BarBartenderProvider.DesignId));
            Assert.That(active.BuildSignature, Has.Length.EqualTo(64));
            Assert.That(active.SourceTriangleCount, Is.InRange(900, 2600));
            Assert.That(active.Renderers.Count, Is.InRange(28, 58));
            Assert.That(
                active.RendererBindings.Count,
                Is.EqualTo(active.Renderers.Count));
            Assert.That(active.ExtraArmChains, Is.Empty);
            Assert.That(active.UsesAuthoredServiceClips, Is.True);
            Assert.That(active.ClipBindings.Count, Is.EqualTo(4));
            Assert.That(active.LeftGripSocket, Is.Not.Null);
            Assert.That(active.LeftVesselSocket, Is.Not.Null);
            Assert.That(active.RightGripSocket, Is.Not.Null);
            Assert.That(active.RightBottleSocket, Is.Not.Null);
            Assert.That(active.VesselGripAnchor, Is.Not.Null);
            Assert.That(active.BottleGripAnchor, Is.Not.Null);
            Assert.That(
                provider.BartenderPrefab
                    .GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                provider.BartenderPrefab
                    .GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                provider.BartenderPrefab
                    .GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                active.LocalBounds.size.y,
                Is.EqualTo(1.75f).Within(0.05f));

            BarBartenderAssetRegistry legacy =
                provider.LegacyBartenderPrefab.GetComponent<
                    BarBartenderAssetRegistry>();
            Assert.That(legacy, Is.Not.Null);
            Assert.That(
                legacy.DesignId,
                Is.EqualTo(BarBartenderProvider.LegacyDesignId));
            Assert.That(legacy.UsesAuthoredServiceClips, Is.False);
            Assert.That(
                legacy.ExtraArmChains.Count,
                Is.EqualTo(
                    BarBartenderAssetRegistry.ExtraArmChainCount));
            var expectedChainIds = new[]
            {
                "Arm2.L",
                "Arm2.R",
                "Arm3.L",
                "Arm3.R"
            };
            var seenGrips = new HashSet<Transform>();
            for (int index = 0;
                 index < legacy.ExtraArmChains.Count;
                 index++)
            {
                BarBartenderArmChain chain =
                    legacy.ExtraArmChains[index];
                Assert.That(
                    chain.ChainId,
                    Is.EqualTo(expectedChainIds[index]));
                Assert.That(chain.ShoulderPivot, Is.Not.Null);
                Assert.That(chain.ElbowPivot, Is.Not.Null);
                Assert.That(chain.WristPivot, Is.Not.Null);
                Assert.That(chain.GripPivot, Is.Not.Null);
                Assert.That(seenGrips.Add(chain.GripPivot), Is.True);
            }
        }

        [Test]
        public void OrdinaryPresentation_ReadsServiceTimelineIntoWaiterClips()
        {
            BarBartenderProvider provider = BarBartenderProvider.Load();
            GameObject instance = Object.Instantiate(
                provider.BartenderPrefab);
            try
            {
                BarBartenderAssetRegistry registry =
                    instance.GetComponent<BarBartenderAssetRegistry>();
                BarBartenderPresentation presentation =
                    instance.AddComponent<BarBartenderPresentation>();
                presentation.Initialize(registry);

                Renderer serviceTowel = null;
                for (int index = 0;
                     index < registry.RendererBindings.Count;
                     index++)
                {
                    BarBartenderRendererBinding binding =
                        registry.RendererBindings[index];
                    if (binding != null &&
                        binding.RendererName == "ACC_ServiceTowel")
                    {
                        serviceTowel = binding.Renderer;
                        break;
                    }
                }

                Assert.That(presentation.UsesOrdinaryRig, Is.True);
                Assert.That(serviceTowel, Is.Not.Null);
                Assert.That(serviceTowel.enabled, Is.True);
                Assert.That(
                    presentation.ChainCount,
                    Is.EqualTo(
                        BarBartenderPresentation.OrdinaryHandCount));
                Assert.That(
                    presentation.GetChainGrip(
                        BarBartenderPresentation
                            .OrdinaryVesselHandIndex),
                    Is.SameAs(registry.VesselGripAnchor));
                Assert.That(
                    presentation.GetChainGrip(
                        BarBartenderPresentation
                            .OrdinaryBottleHandIndex),
                    Is.SameAs(registry.BottleGripAnchor));

                var timeline = new BarDrinkServiceTimeline();
                Assert.That(timeline.BeginOpen(), Is.True);
                presentation.ApplyServiceFrame(
                    timeline.CurrentFrame,
                    leftHandCarriesMenu: true);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Notice));
                Assert.That(serviceTowel.enabled, Is.False,
                    "Menu delivery must free the bartender's left hand towel.");

                timeline.Advance(
                    BarDrinkServiceTimeline
                        .CameraApproachDurationSeconds);
                presentation.ApplyServiceFrame(timeline.CurrentFrame);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Wipe));
                Assert.That(serviceTowel.enabled, Is.True,
                    "Placed-menu browsing must restore the idle towel.");
                Assert.That(timeline.Confirm(), Is.True);
                presentation.ApplyServiceFrame(
                    timeline.CurrentFrame,
                    leftHandCarriesMenu: true);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Walk));
                Assert.That(serviceTowel.enabled, Is.False,
                    "Menu retrieval must free the bartender's left hand towel.");

                timeline.Advance(
                    BarDrinkServiceTimeline
                        .BottlePickupDurationSeconds +
                    BarDrinkServiceTimeline
                        .VesselPlacementDurationSeconds);
                presentation.ApplyServiceFrame(
                    timeline.CurrentFrame);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Pour));

                presentation.SetChainTarget(
                    BarBartenderPresentation
                        .OrdinaryBottleHandIndex,
                    instance.transform.position + Vector3.forward,
                    1f);
                presentation.Advance(
                    BarBartenderPresentation.ReachBlendSeconds);
                Assert.That(
                    presentation.GetChainWeight(
                        BarBartenderPresentation
                            .OrdinaryBottleHandIndex),
                    Is.EqualTo(1f).Within(0.001f));

                presentation.ResetServicePose();
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Wipe));
                Assert.That(serviceTowel.enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
