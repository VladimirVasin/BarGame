using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class FishermanAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type setup = Type.GetType("BarPromenade.Editor.CityPedestrianAssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildLakeFishermanOrThrow", Type.EmptyTypes).Invoke(null, null);
            setup.GetMethod("ValidateLakeFishermanOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type props = Type.GetType("BarPromenade.Editor.CityPedestrianHandPropAssetSetup, BarPromenade.Editor", true);
            props.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Fisherman PNG face, rod grip and shared smoking cycle in the production scene.")]
        [PrebuildSetup(typeof(FishermanAssetsSetup))]
        public IEnumerator FishermanFaceGripAndSmoke()
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
                        var pipe = Object.FindAnyObjectByType<SeacoastFishermanPipeEffect>();
                        Assert.That(fisher, Is.Not.Null);
                        Assert.That(pipe, Is.Not.Null);
                        var body = fisher.Registry;
                        Assert.That(body.HasFaceAtlas, Is.True);
                        Assert.That(body.FaceAtlas.Texture.name, Is.EqualTo("LakeFishermanFaceAtlas"));
                        Assert.That(body.FaceAtlas.Texture, Is.Not.SameAs(body.DetailAtlas));
                        Assert.That(body.Renderers.Any(renderer => renderer.name == "ACC_Nose" ||
                            renderer.name == "ACC_Eye.L" || renderer.name == "ACC_Eye.R"), Is.False);
                        body.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        Assert.That(pipe.ExhaleEffect.IsManualBurstMode, Is.True);
                        Assert.That(pipe.ExhaleEffect.MouthAnchor, Is.SameAs(CityPedestrianHandProps.FindSocket(
                            body.ModelRoot, SeacoastFishermanFactory.ExhaleAnchorName)));
                        var smokingPipe = body.GetComponentsInChildren<CityPedestrianHandPropRegistry>()
                            .Single(prop => prop.Id == CityPedestrianHandPropId.SmokingPipe);
                        Assert.That(smokingPipe.transform.parent.name, Is.EqualTo(SeacoastFishermanFactory.PipeMountAnchorName));
                        Assert.That(smokingPipe.transform.parent.parent, Is.SameAs(body.HeadAnchor));
                        Assert.That(smokingPipe.transform.localPosition, Is.EqualTo(Vector3.zero));
                        Assert.That(smokingPipe.transform.parent.localScale.x, Is.EqualTo(.72f).Within(.0001f));
                        Assert.That(Vector3.Dot(pipe.ExhaleEffect.MouthAnchor.up, smokingPipe.transform.parent.up),
                            Is.GreaterThan(.98f), "The shared smoke uses local Y to exhale away from the face.");
                        Renderer stem = smokingPipe.FindRenderer("ACC_PipeStem");
                        Assert.That(stem, Is.Not.Null);
                        Mesh stemMesh = stem.GetComponent<MeshFilter>().sharedMesh;
                        Assert.That(stemMesh.vertices.Min(vertex => Vector3.Distance(stem.transform.TransformPoint(vertex),
                            pipe.ExhaleEffect.MouthAnchor.position)), Is.LessThan(.012f),
                            "The shared exhale must begin at the painted mouth and pipe inlet, not the old socket.");
                        paused = GameTimeScaleRuntime.AcquirePause();

                        VerifyFishermanSmokeCycle(fisher, pipe);
                        MoveFishermanToBreathPhase(fisher, pipe, .3f);
                        Assert.That(fisher.SetExpression(PlayerFacialExpression.Neutral), Is.True);
                        var faceProperties = new MaterialPropertyBlock();
                        body.FaceAtlas.Renderer.GetPropertyBlock(faceProperties);
                        Assert.That(faceProperties.GetTexture("_BaseMap"), Is.SameAs(body.FaceAtlas.Texture));
                        Assert.That(faceProperties.GetColor("_BaseColor"), Is.EqualTo(Color.white));

                        Vector3 face = body.HeadAnchor.position + Vector3.up * .13f;
                        Transform frame = body.transform;
                        Transform hand = body.ModelRoot.GetComponentsInChildren<Transform>(true)
                            .Single(item => item.name == "hand.L");
                        Transform rightHand = body.ModelRoot.GetComponentsInChildren<Transform>(true)
                            .Single(item => item.name == "hand.R");
                        Transform leftElbow = body.ModelRoot.GetComponentsInChildren<Transform>(true)
                            .Single(item => item.name == "forearm.L");
                        Vector3 gripFocus = (hand.position + rightHand.position + leftElbow.position) / 3f;
                        Assert.That(Vector3.Distance(hand.position, rightHand.position), Is.LessThan(.22f),
                            "The supporting left grip must stay close to the right hand.");
                        Vector3 faceCamera = face + frame.forward * .88f - frame.right * .28f - Vector3.up * .22f;
                        Vector3 coal = pipe.EmberLight.transform.position;
                        return new[]
                        {
                            Shot.At("fisherman-updated-body", face + frame.forward * 1.8f - frame.right * 1.0f + Vector3.up * .12f,
                                face - Vector3.up * .45f, 58f),
                            Shot.At("fisherman-updated-grip", hand.position + frame.forward * .68f - frame.right * .48f + Vector3.up * .20f,
                                hand.position - Vector3.up * .025f, 44f),
                            Shot.At("fisherman-grip-side", gripFocus + frame.right * 1.05f + frame.forward * .22f + Vector3.up * .18f,
                                gripFocus, 46f),
                            Shot.At("fisherman-grip-above", gripFocus + Vector3.up * 1.05f + frame.forward * .2f,
                                gripFocus, 46f),
                            Shot.At("fisherman-inhale", faceCamera, face, 42f,
                                readyWhen: FishermanOnce(() =>
                                {
                                    MoveFishermanToBreathPhase(fisher, pipe, .3f);
                                    pipe.ExhaleEffect.StopAndClear();
                                    Assert.That(pipe.EmberLight.enabled, Is.True);
                                })),
                            Shot.At("fisherman-ember-closeup", coal + frame.forward * .18f - frame.right * .08f + Vector3.up * .09f,
                                coal, 32f),
                            Shot.At("fisherman-exhale", faceCamera, face, 42f,
                                readyWhen: FishermanOnce(() =>
                                {
                                    int bursts = pipe.ExhaleEffect.ManualBurstCount;
                                    MoveFishermanToBreathPhase(fisher, pipe, .80f);
                                    Assert.That(pipe.ExhaleEffect.ManualBurstCount, Is.EqualTo(bursts + 1));
                                    Assert.That(pipe.EmberLight.enabled, Is.False);
                                    pipe.Plume.Simulate(.6f, true, false, false);
                                    Assert.That(pipe.Plume.particleCount, Is.GreaterThan(0));
                                })),
                            Shot.At("fisherman-rest", faceCamera, face, 42f,
                                readyWhen: FishermanOnce(() =>
                                {
                                    MoveFishermanToBreathPhase(fisher, pipe, .95f);
                                    pipe.ExhaleEffect.StopAndClear();
                                    Assert.That(pipe.EmberAmount, Is.Zero);
                                    Assert.That(pipe.EmberLight.enabled, Is.False);
                                })),
                        };
                    });
            }
            finally { paused?.Dispose(); }
        }

        private static void VerifyFishermanSmokeCycle(SeacoastFishermanPresentation fisher, SeacoastFishermanPipeEffect pipe)
        {
            MoveFishermanToBreathPhase(fisher, pipe, .05f);
            int initialBursts = pipe.ExhaleEffect.ManualBurstCount;
            double end = fisher.BreathTime + 4d;
            double nextSleeveCheck = fisher.BreathTime;
            int steps = 0;
            while (fisher.BreathTime < end && steps++ < 2500)
            {
                fisher.Advance(.005f);
                pipe.Synchronize();
                if (fisher.BreathTime >= nextSleeveCheck)
                {
                    AssertFishermanSleevesDoNotIntersect(fisher.Registry);
                    nextSleeveCheck += .5d;
                }
                if (fisher.BreathPhase >= .5f)
                {
                    Assert.That(pipe.EmberAmount, Is.Zero);
                    Assert.That(pipe.EmberLight.enabled, Is.False);
                }
            }
            Assert.That(steps, Is.LessThan(2500));
            Assert.That(pipe.ExhaleEffect.ManualBurstCount, Is.EqualTo(initialBursts + 4));
            double time = fisher.ClipTimeSeconds;
            int bursts = pipe.ExhaleEffect.ManualBurstCount;
            for (int index = 0; index < 8; index++) { fisher.Advance(0f); pipe.Synchronize(); }
            Assert.That(fisher.ClipTimeSeconds, Is.EqualTo(time));
            Assert.That(pipe.ExhaleEffect.ManualBurstCount, Is.EqualTo(bursts));
            pipe.enabled = false;
            Assert.That(pipe.EmberLight.enabled, Is.False);
            Assert.That(pipe.Plume.particleCount, Is.Zero);
            pipe.enabled = true;
            pipe.Synchronize();
            Assert.That(pipe.ExhaleEffect.ManualBurstCount, Is.EqualTo(bursts));
        }

        private static void AssertFishermanSleevesDoNotIntersect(CityPedestrianAssetRegistry body)
        {
            string[] names = { "CLO_Sleeve", "CLO_SleeveLower", "CLO_SleeveCuff" };
            var meshes = new Mesh[6];
            var colliders = new MeshCollider[6];
            var host = new GameObject("Fisherman sleeve contact proof");
            host.transform.position = Vector3.down * 1000f;
            try
            {
                for (int side = 0; side < 2; side++)
                for (int part = 0; part < names.Length; part++)
                {
                    int index = side * 3 + part;
                    string name = names[part] + (side == 0 ? ".L" : ".R");
                    var skin = (SkinnedMeshRenderer)body.Renderers.Single(renderer => renderer.name == name);
                    Mesh mesh = meshes[index] = new Mesh { name = name };
                    // Match the shared contact/foot probes: true compensates
                    // imported FBX units before the full transform to metres.
                    skin.BakeMesh(mesh, true);
                    mesh.vertices = mesh.vertices.Select(vertex => skin.transform.TransformPoint(vertex) - body.transform.position).ToArray();
                    mesh.RecalculateBounds();
                    Assert.That(mesh.bounds.size.magnitude, Is.InRange(.05f, .8f),
                        $"{name} must be measured in world metres.");
                    var partHost = new GameObject(name);
                    partHost.transform.SetParent(host.transform, false);
                    MeshCollider collider = colliders[index] = partHost.AddComponent<MeshCollider>();
                    collider.convex = true;
                    collider.sharedMesh = mesh;
                }
                // These are convex hulls of the actual posed sleeve meshes,
                // including cuffs, rather than distances between bare bones.
                for (int left = 0; left < 3; left++)
                for (int right = 3; right < 6; right++)
                {
                    bool overlaps = Physics.ComputePenetration(colliders[left], Vector3.zero, Quaternion.identity,
                        colliders[right], Vector3.zero, Quaternion.identity, out _, out float depth);
                    Assert.That(overlaps && depth > .003f, Is.False,
                        $"{meshes[left].name} penetrates {meshes[right].name} by {depth:F4} m.");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (Mesh mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        private static void MoveFishermanToBreathPhase(SeacoastFishermanPresentation fisher,
            SeacoastFishermanPipeEffect pipe, float phase)
        {
            if (Mathf.Abs(fisher.BreathPhase - phase) < .004f) return;
            double target = Math.Floor(fisher.BreathTime) + phase;
            if (target < fisher.BreathTime) target += 1d;
            int steps = 0;
            while (fisher.BreathTime < target && steps++ < 600)
            {
                fisher.Advance(.005f);
                pipe.Synchronize();
            }
            Assert.That(steps, Is.LessThan(600));
        }

        private static Func<bool> FishermanOnce(Action action)
        {
            bool complete = false;
            return () =>
            {
                if (complete) return true;
                action();
                complete = true;
                // The shared capture loop yields while false. Give Unity a frame
                // to update skinned geometry after manually advancing the graph;
                // rigid props otherwise render beside the previous cached skin.
                return false;
            };
        }

    }
}
