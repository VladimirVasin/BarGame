using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBusDriverAssetContractTests
    {
        private const string ModelPath =
            "Assets/Vehicles/Drivers/Models/CityBusDriver3D.fbx";
        private const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        private const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";

        [Test]
        public void ProductionPrefab_ExposesPassiveProceduralDriverContract()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(
                importer.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther));
            Assert.That(importer.sourceAvatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(importer.sourceAvatar),
                Is.EqualTo(PlayerModelPath));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(
                AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal)),
                Is.Empty,
                "The driver FBX must stay animation-free.");

            GameObject prefab = CityBusDriverResources.LoadPrefab();
            GameObject playerPrefab = Player3DResources.LoadPrefab();
            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(playerPrefab, Is.Not.Null);
            Assert.That(sharedMaterial, Is.Not.Null);

            CityBusDriverAssetRegistry registry =
                prefab.GetComponent<CityBusDriverAssetRegistry>();
            Player3DAssetRegistry playerRegistry =
                playerPrefab.GetComponent<Player3DAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(playerRegistry, Is.Not.Null);
            Assert.That(registry.Animator, Is.Not.Null);
            Assert.That(
                registry.Animator.avatar,
                Is.SameAs(playerRegistry.Animator.avatar));
            Assert.That(registry.Animator.applyRootMotion, Is.False);
            Assert.That(registry.Animator.runtimeAnimatorController, Is.Null);
            Assert.That(
                registry.Animator.cullingMode,
                Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));

            Assert.That(registry.DesignId, Is.EqualTo("long_eyes_driver_v1"));
            Assert.That(registry.SourceTriangleCount, Is.EqualTo(1496));
            Assert.That(registry.Renderers.Count, Is.EqualTo(48));
            Assert.That(
                registry.LocalBounds.min.y,
                Is.EqualTo(0f).Within(0.025f));
            Assert.That(
                registry.LocalBounds.size.y,
                Is.EqualTo(1.75f).Within(0.035f));

            AssertProceduralBindings(registry);
            AssertLongEyesAndPoseablePupils(registry);
            Assert.That(
                registry.ModelRoot
                    .GetComponentsInChildren<Transform>(true)
                    .Single(transform => transform.name == "root")
                    .GetComponentsInChildren<Transform>(true).Length,
                Is.EqualTo(31));

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(registry.Renderers, Is.All.Not.Null);
            Assert.That(
                registry.Renderers.All(renderer =>
                    renderer.sharedMaterials.Length == 1 &&
                    renderer.sharedMaterial == sharedMaterial),
                Is.True);
        }

        private static void AssertProceduralBindings(
            CityBusDriverAssetRegistry registry)
        {
            Transform[] bindings =
            {
                registry.ModelRoot,
                registry.Pelvis,
                registry.Spine,
                registry.Chest,
                registry.Neck,
                registry.Head,
                registry.FaceEyeLeft,
                registry.FaceEyeRight,
                registry.LeftUpperArm,
                registry.LeftForearm,
                registry.LeftHand,
                registry.LeftGripSocket,
                registry.LeftThigh,
                registry.LeftShin,
                registry.LeftFoot,
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand,
                registry.RightGripSocket,
                registry.RightThigh,
                registry.RightShin,
                registry.RightFoot
            };
            Assert.That(bindings, Is.All.Not.Null);
            Assert.That(registry.FaceEyeLeft.name, Is.EqualTo("face.eye.L"));
            Assert.That(registry.FaceEyeRight.name, Is.EqualTo("face.eye.R"));
            Assert.That(registry.LeftGripSocket.name, Is.EqualTo("SOCKET_Grip.L"));
            Assert.That(registry.RightGripSocket.name, Is.EqualTo("SOCKET_Grip.R"));
            Assert.That(registry.FaceEyeLeft.parent, Is.SameAs(registry.Head));
            Assert.That(registry.FaceEyeRight.parent, Is.SameAs(registry.Head));
            Assert.That(registry.LeftGripSocket.parent, Is.SameAs(registry.LeftHand));
            Assert.That(registry.RightGripSocket.parent, Is.SameAs(registry.RightHand));
        }

        private static void AssertLongEyesAndPoseablePupils(
            CityBusDriverAssetRegistry registry)
        {
            foreach (string side in new[] { "L", "R" })
            {
                CityBusDriverRendererBinding eye = registry.RendererBindings
                    .Single(binding =>
                        binding.RendererName == $"FACE_EyeWhite.{side}");
                CityBusDriverRendererBinding pupil = registry.RendererBindings
                    .Single(binding =>
                        binding.RendererName == $"FACE_Pupil.{side}");
                Assert.That(eye.Role, Is.EqualTo("long_horizontal_eye"));
                Assert.That(pupil.Role, Is.EqualTo("visible_eye_pupil"));
                Assert.That(pupil.BoneName, Is.EqualTo($"face.eye.{side}"));
                Assert.That(eye.Renderer, Is.TypeOf<SkinnedMeshRenderer>());
                Mesh eyeMesh =
                    ((SkinnedMeshRenderer)eye.Renderer).sharedMesh;
                Assert.That(eyeMesh, Is.Not.Null);
                Assert.That(
                    eyeMesh.bounds.size.x,
                    Is.GreaterThan(eyeMesh.bounds.size.y * 1.9f),
                    $"The {side} eye must read as a long horizontal shape.");
                Assert.That(
                    pupil.BaseColor.maxColorComponent,
                    Is.LessThan(0.08f),
                    $"The {side} pupil must remain visibly dark.");
            }
        }
    }
}
