using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PrincipalNormalNpcAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type setup = Type.GetType("BarPromenade.Editor.PrincipalNormalNpcAssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            setup.GetMethod("ValidateOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused ordinary actor art review: imported detail, real scene placement and working poses.")]
        [PrebuildSetup(typeof(PrincipalNormalNpcAssetsSetup))]
        public IEnumerator PrincipalNormalNpcs()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)(360d / GameTimeState.GameMinutesPerRealSecond));
            IDisposable paused = null;
            try
            {
                yield return Capture(SceneIds.City,
                    () =>
                    {
                        var city = Object.FindAnyObjectByType<CityGameRoot>();
                        return city != null && city.IsInitialized ? city : null;
                    },
                    () =>
                    {
                        var fisher = Object.FindAnyObjectByType<SeacoastFishermanPresentation>();
                        Assert.That(fisher, Is.Not.Null);
                        var body = fisher.GetComponent<CityPedestrianAssetRegistry>();
                        Assert.That(body.DesignId, Is.EqualTo("lake_fisherman_v1"));
                        Assert.That(body.SourceTriangleCount, Is.InRange(4500, 8000));
                        body.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        var city = Object.FindAnyObjectByType<CityGameRoot>();
                        // The production bus is pooled outside the player's view.
                        // Place that same instance on its authored route for the cabin shots.
                        city.Bus.enabled = false;
                        var bus = city.Bus.GetComponentInChildren<CityBusPresentation>(true);
                        Assert.That(bus, Is.Not.Null);
                        if (!city.Bus.Actor.IsSpawned)
                        {
                            city.Bus.Actor.PrepareSpawn(city.BusPlan, city.BusPlan.SpawnAnchors[0], 0x504f5254u);
                            city.Bus.Actor.BindPresentation(bus);
                        }
                        var driver = bus.DriverPresentation;
                        Assert.That(driver, Is.Not.Null);
                        Assert.That(driver.IsInitialized, Is.True);
                        Assert.That(driver.Registry.SourceTriangleCount, Is.InRange(4500, 8000));
                        driver.ApplyPose(0f, 0f, 0f);
                        paused = GameTimeScaleRuntime.AcquirePause();
                        Vector3 fisherFace = body.HeadAnchor.position + Vector3.up * .13f;
                        var shots = new List<Shot>
                        {
                            Shot.At("normal-fisherman-body", fisherFace + body.transform.forward * 1.65f - body.transform.right * .85f + Vector3.up * .15f,
                                fisherFace - Vector3.up * .55f, 58f, delayFrames: 2),
                            Shot.At("normal-fisherman-face", fisherFace + body.transform.forward * .82f - body.transform.right * .32f + Vector3.up * .09f,
                                fisherFace, 42f, delayFrames: 2),
                        };
                        Vector3 head = driver.Registry.Head.position;
                        Transform frame = driver.Registry.transform;
                        shots.Add(Shot.At("normal-driver-work", head + frame.forward * .95f + frame.right * .60f + Vector3.up * .08f,
                            head - Vector3.up * .30f, 58f, delayFrames: 2));
                        shots.Add(Shot.At("normal-driver-face", head + frame.forward * .70f + frame.right * .25f,
                            head, 42f, delayFrames: 2));
                        return shots.ToArray();
                    });
            }
            finally { paused?.Dispose(); }

            GameSessionState.EnterBar("normal-npc-detail-capture");
            yield return Capture(SceneIds.BarInterior,
                () =>
                {
                    var root = Object.FindAnyObjectByType<BarInteriorRoot>();
                    return root != null && root.IsInitialized ? root : null;
                },
                () =>
                {
                    var actor = Object.FindAnyObjectByType<BarBartenderPresentation>();
                    Assert.That(actor, Is.Not.Null);
                    Assert.That(actor.Registry.DesignId, Is.EqualTo("bar_bartender_v2"));
                    Assert.That(actor.Registry.SourceTriangleCount, Is.InRange(4500, 8000));
                    actor.Registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    return PrincipalActorShots("normal-bartender", actor.Registry.transform, actor.Registry.Head);
                });

            yield return Capture(SceneIds.SupermarketInterior,
                () =>
                {
                    var root = Object.FindAnyObjectByType<SupermarketInteriorRoot>();
                    return root != null && root.IsInitialized ? root : null;
                },
                () =>
                {
                    var actor = Object.FindAnyObjectByType<SupermarketCashierAssetRegistry>();
                    Assert.That(actor, Is.Not.Null);
                    Assert.That(actor.DesignId, Is.EqualTo("supermarket_cashier_v1"));
                    Assert.That(actor.SourceTriangleCount, Is.InRange(4500, 8000));
                    return PrincipalActorShots("normal-cashier", actor.transform, actor.Head);
                });

            yield return Capture(SceneIds.MothersHouseInterior,
                () =>
                {
                    var root = Object.FindAnyObjectByType<MothersHouseInteriorRoot>();
                    return root != null && root.IsInitialized ? root : null;
                },
                () =>
                {
                    var root = Object.FindAnyObjectByType<MothersHouseInteriorRoot>();
                    Assert.That(root.Mother.Registry.SourceTriangleCount, Is.InRange(4500, 8000));
                    return MothersHouseMotherShots(root);
                });
        }

        private static Shot[] PrincipalActorShots(string label, Transform frame, Transform head)
        {
            Vector3 focus = head.position;
            return new[]
            {
                Shot.At(label + "-body", focus + frame.forward * 2.4f + frame.right * .65f - Vector3.up * .15f,
                    focus - Vector3.up * .60f, 48f, delayFrames: 8),
                Shot.At(label + "-face", focus + frame.forward * .90f + frame.right * .25f + Vector3.up * .04f,
                    focus, 38f, delayFrames: 8),
            };
        }
    }
}
