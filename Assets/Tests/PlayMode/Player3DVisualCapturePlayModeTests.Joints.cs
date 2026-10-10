using System.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class JointSurfaceAssetsSetup : IPrebuildSetup
    {
        private static bool prepared;

        public void Setup()
        {
#if UNITY_EDITOR
            if (prepared) return;
            // Geometry and derived assets are already published by Blender.
            // These hero tests need its consumer, not unrelated NPC/gore builds.
            Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            prepared = true;
#endif
        }
    }

    public sealed partial class Player3DVisualCapturePlayModeTests
    {
        [UnityTest]
        [PrebuildSetup(typeof(JointSurfaceAssetsSetup))]
        public IEnumerator ProductionRig_RendersBareAndLooseClothedJointBends()
        {
            Player3DAssetRegistry registry = CreateJointCaptureRig();
            registry.Animator.enabled = false;
            PlayerJacketCloth jacket = registry.GetComponent<PlayerJacketCloth>();
            jacket.enabled = false;
            PlayerBootDeformation boots = registry.GetComponent<PlayerBootDeformation>();
            boots.enabled = false;
            PlayerHair hair = registry.GetComponent<PlayerHair>();
            if (hair != null) hair.enabled = false;
            var rest = registry.GetComponentsInChildren<Transform>(true).ToDictionary(bone => bone, bone => bone.localRotation);
            Vector3 rigPosition = registry.transform.position;
            contactSheet = new Texture2D(TileSize * 4, TileSize * 9, TextureFormat.RGBA32, false, true)
            { name = "Test joint bend sheet", filterMode = FilterMode.Point };
            float[] angles = { 0f, 45f, 90f, 135f };
            Camera camera = cameraObject.GetComponent<Camera>();
            for (int row = 0; row < 4; row++)
            {
                bool bare = row % 2 == 0;
                bool knee = row >= 2;
                PlayerWardrobe.AppearanceLease undressed = bare ? registry.GetComponent<PlayerWardrobe>().Undress() : null;
                try
                {
                    for (int column = 0; column < angles.Length; column++)
                    {
                        foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
                        Transform joint = BendCaptureJoint(registry, knee, angles[column]);
                        registry.GetComponent<CharacterJointDeformation>().ApplyPose();
                        jacket.RequestReset();
                        jacket.ApplyAt(0d, false, false, new WindSample(0f, 0f));
                        yield return null;
                        Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward).normalized;
                        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                        Vector3 target = joint.position;
                        Vector3 offset = (forward * .75f + right * .65f + Vector3.up * .1f).normalized;
                        camera.transform.position = target + offset * 3f;
                        camera.transform.rotation = Quaternion.LookRotation(-offset, Vector3.up);
                        camera.orthographicSize = knee ? .32f : .29f;
                        lightObject.transform.forward = camera.transform.forward;
                        Texture2D tile = CaptureJoint(camera);
                        try
                        {
                            Assert.That(CountForegroundPixels(tile, tile.GetPixel(0, 0)), Is.GreaterThan(400),
                                (bare ? "Bare " : "Clothed ") + (knee ? "knee" : "elbow") + " at " + angles[column] + " degrees must render.");
                            contactSheet.SetPixels(column * TileSize, (8 - row) * TileSize, TileSize, TileSize, tile.GetPixels());
                        }
                        finally { Object.Destroy(tile); }
                    }
                }
                finally { undressed?.Dispose(); }
            }
            foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
            registry.GetComponent<CharacterJointDeformation>().ApplyPose();
            jacket.RequestReset();
            jacket.ApplyAt(0d, false, false, new WindSample(0f, 0f));
            for (int column = 0; column < 4; column++)
            {
                bool bare = column % 2 == 0;
                PlayerWardrobe.AppearanceLease undressed = bare ? registry.GetComponent<PlayerWardrobe>().Undress() : null;
                try
                {
                    yield return null;
                    Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward).normalized;
                    Vector3 direction = column < 2 ? forward : Vector3.Cross(Vector3.up, forward).normalized;
                    Vector3 target = registry.Anchors.Chest.position - Vector3.up * .08f;
                    camera.transform.position = target + direction * 3f;
                    camera.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
                    camera.orthographicSize = .58f;
                    lightObject.transform.forward = camera.transform.forward;
                    Texture2D tile = CaptureJoint(camera);
                    try
                    {
                        Assert.That(CountForegroundPixels(tile, tile.GetPixel(0, 0)), Is.GreaterThan(400),
                            "The revised torso and shoulder silhouette must render from front and side.");
                        contactSheet.SetPixels(column * TileSize, TileSize * 4, TileSize, TileSize, tile.GetPixels());
                    }
                    finally { Object.Destroy(tile); }
                }
                finally { undressed?.Dispose(); }
            }
            Transform head = registry.GetComponentsInChildren<Transform>(true).Single(bone => bone.name == "head");
            Transform neck = registry.GetComponentsInChildren<Transform>(true).Single(bone => bone.name == "neck");
            for (int column = 0; column < 4; column++)
            {
                foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
                Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                head.rotation = Quaternion.AngleAxis(column < 2 ? 45f : 20f, column < 2 ? Vector3.up : right) * head.rotation;
                registry.GetComponent<CharacterJointDeformation>().ApplyPose();
                jacket.RequestReset();
                jacket.ApplyAt(0d, false, false, new WindSample(0f, 0f));
                PlayerWardrobe.AppearanceLease undressed = column % 2 == 0 ? registry.GetComponent<PlayerWardrobe>().Undress() : null;
                try
                {
                    yield return null;
                    Vector3 target = neck.position + Vector3.up * .045f;
                    camera.transform.position = target + forward * 3f;
                    camera.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
                    camera.orthographicSize = .25f;
                    lightObject.transform.forward = camera.transform.forward;
                    Texture2D tile = CaptureJoint(camera);
                    try
                    {
                        Assert.That(CountForegroundPixels(tile, tile.GetPixel(0, 0)), Is.GreaterThan(400),
                            "The neck must render with turned and tilted head poses.");
                        contactSheet.SetPixels(column * TileSize, TileSize * 3, TileSize, TileSize, tile.GetPixels());
                    }
                    finally { Object.Destroy(tile); }
                }
                finally { undressed?.Dispose(); }
            }
            foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
            registry.GetComponent<CharacterJointDeformation>().ApplyPose();
            PlayerWardrobe wardrobe = registry.GetComponent<PlayerWardrobe>();
            PlayerWardrobe.OutfitSnapshot outfit = wardrobe.CaptureOutfit();
            for (int column = 0; column < 4; column++)
            {
                bool bare = column % 2 == 0;
                wardrobe.SetSlot("jacket", null);
                PlayerWardrobe.AppearanceLease undressed = bare ? wardrobe.Undress() : null;
                try
                {
                    yield return null;
                    Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward).normalized;
                    Vector3 direction = column < 2 ? -forward : Vector3.Cross(Vector3.up, forward).normalized;
                    Vector3 target = registry.Anchors.Pelvis.position;
                    camera.transform.position = target + direction * 3f;
                    camera.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
                    camera.orthographicSize = .34f;
                    lightObject.transform.forward = camera.transform.forward;
                    Texture2D tile = CaptureJoint(camera);
                    try
                    {
                        Assert.That(CountForegroundPixels(tile, tile.GetPixel(0, 0)), Is.GreaterThan(400),
                            "Pelvic and gluteal volume must render from rear and side, bare and in trousers.");
                        contactSheet.SetPixels(column * TileSize, TileSize * 2, TileSize, TileSize, tile.GetPixels());
                    }
                    finally { Object.Destroy(tile); }
                }
                finally { undressed?.Dispose(); wardrobe.RestoreOutfit(outfit); }
            }
            var rightFoot = boots.Bindings.Single(binding => binding.Side == FootSide.Right);
            float supportY = rightFoot.Foot.TransformPoint(rightFoot.BallContactLocal).y;
            wardrobe.SetSlot("trousers", null);
            try
            {
                for (int column = 0; column < 4; column++)
                {
                    foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
                    registry.transform.position = rigPosition;
                    FootGroundSample support = Player3DJointSurfaceTests.PoseBootRoll(registry, column * 15f, supportY);
                    boots.ApplyPose(support, support);
                    Player3DJointSurfaceTests.GroundCompletedBoot(registry,
                        rightFoot.Shapes.Single(shape => shape.Renderer.name == "CLO_BootSole.R").Renderer, supportY);
                    yield return null;
                    Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward).normalized;
                    Vector3 direction = (Vector3.Cross(Vector3.up, forward) + forward * .15f).normalized;
                    Vector3 target = rightFoot.Foot.TransformPoint(rightFoot.BallContactLocal) + Vector3.up * .10f;
                    camera.transform.position = target + direction * 3f;
                    camera.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
                    camera.orthographicSize = .25f;
                    lightObject.transform.forward = camera.transform.forward;
                    Texture2D tile = CaptureJoint(camera);
                    try
                    {
                        Assert.That(CountForegroundPixels(tile, tile.GetPixel(0, 0)), Is.GreaterThan(400),
                            "The burgundy lace-up boot and its supported forefoot bend must render.");
                        contactSheet.SetPixels(column * TileSize, TileSize, TileSize, TileSize, tile.GetPixels());
                    }
                    finally { Object.Destroy(tile); }
                }
            }
            finally
            {
                wardrobe.RestoreOutfit(outfit);
                registry.transform.position = rigPosition;
                foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
                boots.ApplyPose(FootGroundSample.None, FootGroundSample.None);
            }
            var fabricFailures = new List<string>();
            for (int column = 0; column < 4; column++)
            {
                foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
                if (column > 0) Player3DJointSurfaceTests.SetBend(registry, "thigh.R", column == 1 ? 45f : column == 2 ? 90f : 60f);
                if (column == 3)
                {
                    Player3DJointSurfaceTests.SetBend(registry, "thigh.L", 60f);
                    Player3DJointSurfaceTests.SetBend(registry, "shin.R", 90f);
                    Player3DJointSurfaceTests.SetBend(registry, "shin.L", 90f);
                }
                registry.GetComponent<CharacterJointDeformation>().ApplyPose();
                jacket.RequestReset();
                for (int frame = 0; frame < 20; frame++) jacket.ApplyAt(frame / 60d, false, false, new WindSample(0f, 0f));
                yield return null;
                Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 direction = (right + forward * .25f).normalized;
                Vector3 target = registry.Anchors.Pelvis.position;
                camera.transform.position = target + direction * 3f;
                camera.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
                camera.orthographicSize = .43f;
                lightObject.transform.forward = camera.transform.forward;
                Texture2D tile = CaptureJoint(camera);
                try
                {
                    Assert.That(CountForegroundPixels(tile, tile.GetPixel(0, 0)), Is.GreaterThan(400));
                    contactSheet.SetPixels(column * TileSize, 0, TileSize, TileSize, tile.GetPixels());
                    string[] poses = { "standing", "hip45", "hip90", "crouch" };
                    string proofDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
                    Directory.CreateDirectory(proofDirectory);
                    File.WriteAllBytes(Path.Combine(proofDirectory, "player-3d-lower-jacket-" + poses[column] + ".png"), tile.EncodeToPNG());
                    try { Player3DJointSurfaceTests.AssertJacketHemClear(registry, poses[column]); }
                    catch (AssertionException failure)
                    {
                        Player3DJointSurfaceTests.WriteContactReplay(registry, "thigh.R", column == 2 ? 90f : column == 3 ? 60f : column * 45f);
                        fabricFailures.Add(failure.Message);
                    }
                }
                finally { Object.Destroy(tile); }
            }
            contactSheet.Apply(false, false);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, "player-3d-joint-surface-sheet.png");
            File.WriteAllBytes(output, contactSheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(directory, "player-3d-joint-surface-sheet.txt"),
                "Columns: straight, 45 degrees, 90 degrees, 135 degrees.\nRows: bare elbow, jacket elbow, bare knee, trouser knee.\nTorso row: bare front, dressed front, bare side, dressed side.\nNeck row: bare turn, dressed turn, bare tilt, dressed tilt.\nPelvis row: bare rear, trouser rear, bare side, trouser side (jacket removed).\nFootwear row: supported forefoot roll 0, 15, 30, 45 degrees (trousers removed).\nLower jacket row: standing, hip45, hip90, crouch.\n");
            Assert.That(new FileInfo(output).Length, Is.GreaterThan(4096));
            foreach (string side in new[] { "L", "R" })
            {
                foreach (KeyValuePair<Transform, Quaternion> pose in rest) pose.Key.localRotation = pose.Value;
                Player3DJointSurfaceTests.SetBend(registry, "thigh." + side, 135f);
                registry.GetComponent<CharacterJointDeformation>().ApplyPose();
                jacket.RequestReset();
                for (int frame = 0; frame < 20; frame++) jacket.ApplyAt(frame / 60d, false, false, new WindSample(0f, 0f));
                yield return null;
                Vector3 forward = registry.transform.TransformDirection(registry.Metrics.LocalForward).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 direction = ((side == "L" ? -right : right) + forward * .25f).normalized;
                camera.transform.position = registry.Anchors.Pelvis.position + direction * 3f;
                camera.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
                lightObject.transform.forward = camera.transform.forward;
                Texture2D tile = CaptureJoint(camera);
                try
                {
                    Assert.That(CountForegroundPixels(tile, tile.GetPixel(0, 0)), Is.GreaterThan(400));
                    File.WriteAllBytes(Path.Combine(directory, "player-3d-lower-jacket-hip135-" + side + ".png"), tile.EncodeToPNG());
                    try { Player3DJointSurfaceTests.AssertJacketHemClear(registry, "hip135 " + side); }
                    catch (AssertionException failure)
                    {
                        Player3DJointSurfaceTests.WriteContactReplay(registry, "thigh." + side, 135f);
                        fabricFailures.Add(failure.Message);
                    }
                }
                finally { Object.Destroy(tile); }
            }
            Assert.That(fabricFailures, Is.Empty, string.Join("\n", fabricFailures));
        }

        private Texture2D CaptureJoint(Camera camera)
        {
            Texture2D result = Capture(camera);
            // The render target contains linear light; PNG viewers expect sRGB.
            Color[] pixels = result.GetPixels();
            for (int index = 0; index < pixels.Length; index++) pixels[index] = pixels[index].gamma;
            result.SetPixels(pixels);
            result.Apply(false, false);
            return result;
        }

        private Player3DAssetRegistry CreateJointCaptureRig()
        {
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            renderSettingsCaptured = true;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.52f, .54f, .58f);
            RenderSettings.fog = false;
            const int layer = 28;
            cameraObject = new GameObject("Test joint capture camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 1f;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 20f;
            camera.allowHDR = camera.allowMSAA = false;
            camera.cullingMask = 1 << layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.2f, .22f, .26f);
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = true;
            data.volumeLayerMask = 0;
            renderTarget = new RenderTexture(TileSize, TileSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "Test joint capture target", antiAliasing = 1 };
            renderTarget.Create();
            camera.targetTexture = renderTarget;
            lightObject = new GameObject("Test joint capture light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.color = new Color(1f, .86f, .74f);
            light.shadows = LightShadows.Hard;
            light.cullingMask = 1 << layer;
            RenderSettings.sun = light;
            playerRoot = new GameObject("Test joint capture rig");
            Player3DAssetRegistry registry = Player3DResources.Instantiate(playerRoot.transform);
            SetLayerRecursively(playerRoot.transform, layer);
            return registry;
        }

        private static Transform BendCaptureJoint(Player3DAssetRegistry registry, bool knee, float angle)
        {
            string jointName = knee ? "shin.R" : "forearm.R";
            string childName = knee ? "foot.R" : "hand.R";
            Transform[] bones = registry.GetComponentsInChildren<Transform>(true);
            Transform joint = bones.Single(bone => bone.name == jointName);
            Transform child = bones.Single(bone => bone.name == childName);
            Vector3 incoming = (joint.position - joint.parent.position).normalized;
            joint.rotation = Quaternion.FromToRotation(child.position - joint.position, incoming) * joint.rotation;
            Vector3 bendDirection = registry.transform.TransformDirection(registry.Metrics.LocalForward) * (knee ? -1f : 1f);
            Vector3 axis = Vector3.Cross(incoming, bendDirection).normalized;
            joint.rotation = Quaternion.AngleAxis(angle, axis) * joint.rotation;
            return joint;
        }
    }
}
