using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class CityBusAssetImportTests
    {
        private const string ModelPath =
            "Assets/Vehicles/Models/CityBus3D.fbx";
        private const string ManifestPath =
            "Assets/Vehicles/Models/CityBus3D.json";

        [Test]
        public void ProductionMidibus_HasArticulationInteriorAndMeterScale()
        {
            TextAsset manifestAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(manifestAsset, Is.Not.Null);
            CityBusManifest manifest =
                JsonUtility.FromJson<CityBusManifest>(manifestAsset.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.design_id, Is.EqualTo("road_v2_midibus_v1"));
            Assert.That(manifest.forward_axis, Is.EqualTo("-Y"));
            Assert.That(manifest.unity_runtime_forward_axis, Is.EqualTo("+Z"));
            Assert.That(manifest.visible_interior, Is.True);
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);
            Assert.That(manifest.mesh_count, Is.EqualTo(manifest.parts.Length));
            Assert.That(manifest.triangle_count, Is.InRange(900, 12000));
            Assert.That(manifest.passenger_seat_count, Is.GreaterThanOrEqualTo(10));
            Assert.That(manifest.dimensions_m.length, Is.EqualTo(8.25f));
            Assert.That(manifest.dimensions_m.width, Is.EqualTo(2.38f));
            Assert.That(manifest.dimensions_m.height, Is.EqualTo(2.95f));
            Assert.That(manifest.dimensions_m.wheelbase, Is.EqualTo(4.50f));
            Assert.That(manifest.dimensions_m.wheel_radius, Is.EqualTo(0.43f));

            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));

            GameObject prefab = CityBusResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(prefab),
                Is.EqualTo("Assets/Resources/Vehicles/CityBus3D.prefab"));
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                CityBusAssetRegistry registry =
                    instance.GetComponent<CityBusAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.ModelRoot, Is.Not.Null);
                Assert.That(registry.Body, Is.Not.Null);
                Assert.That(registry.FrontDoor, Is.Not.Null);
                Assert.That(registry.RearDoor, Is.Not.Null);
                Assert.That(registry.FrontLeftSteeringPivot, Is.Not.Null);
                Assert.That(registry.FrontRightSteeringPivot, Is.Not.Null);
                Assert.That(
                    registry.FrontLeftWheel.parent,
                    Is.SameAs(registry.FrontLeftSteeringPivot));
                Assert.That(
                    registry.FrontRightWheel.parent,
                    Is.SameAs(registry.FrontRightSteeringPivot));
                Assert.That(
                    registry.PassengerSeatAnchors.Count,
                    Is.EqualTo(manifest.passenger_seat_count));
                Assert.That(registry.Renderers.Count, Is.EqualTo(manifest.mesh_count));
                Assert.That(registry.Headlights, Is.Not.Empty);
                Assert.That(registry.TailLights, Is.Not.Empty);
                Assert.That(registry.CabinLights, Is.Not.Empty);
                Assert.That(
                    registry.RendererBindings.Any(
                        binding => binding.Role == "passenger_seats"),
                    Is.True);
                Assert.That(
                    registry.RendererBindings.Any(
                        binding => binding.Role == "handrails"),
                    Is.True);
                Assert.That(
                    registry.RendererBindings.Any(
                        binding => binding.Role == "steering_wheel"),
                    Is.True);
                Assert.That(registry.LocalBounds.min.y, Is.EqualTo(0f).Within(0.03f));
                Assert.That(
                    registry.LocalBounds.size.y,
                    Is.EqualTo(2.95f).Within(0.04f));
                Assert.That(
                    registry.LocalBounds.size.z,
                    Is.EqualTo(8.25f).Within(0.16f));
                Assert.That(registry.Dimensions.Length, Is.EqualTo(8.25f));
                Assert.That(registry.Dimensions.Width, Is.EqualTo(2.38f));
                Assert.That(registry.SourceTriangleCount, Is.EqualTo(3780));
                Assert.That(registry.BuildSignature, Has.Length.EqualTo(64));
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Serializable]
        private sealed class CityBusManifest
        {
            public string design_id;
            public string forward_axis;
            public string unity_runtime_forward_axis;
            public CityBusDimensionsManifest dimensions_m;
            public int mesh_count;
            public int triangle_count;
            public bool colliders;
            public int animation_count;
            public bool visible_interior;
            public int passenger_seat_count;
            public CityBusPartManifest[] parts;
        }

        [Serializable]
        private sealed class CityBusDimensionsManifest
        {
            public float length;
            public float width;
            public float height;
            public float wheelbase;
            public float wheel_radius;
        }

        [Serializable]
        private sealed class CityBusPartManifest
        {
            public string name;
        }
    }
}
