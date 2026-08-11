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
                Assert.That(registry.FrontDoorForwardLeaf, Is.Not.Null);
                Assert.That(registry.FrontDoorRearwardLeaf, Is.Not.Null);
                Assert.That(registry.RearDoorForwardLeaf, Is.Not.Null);
                Assert.That(registry.RearDoorRearwardLeaf, Is.Not.Null);
                Assert.That(
                    registry.FrontDoorForwardLeaf.parent,
                    Is.SameAs(registry.Body));
                Assert.That(
                    registry.FrontDoorRearwardLeaf.parent,
                    Is.SameAs(registry.Body));
                Assert.That(
                    registry.RearDoorForwardLeaf.parent,
                    Is.SameAs(registry.Body));
                Assert.That(
                    registry.RearDoorRearwardLeaf.parent,
                    Is.SameAs(registry.Body));
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
                Assert.That(
                    registry.SourceTriangleCount,
                    Is.EqualTo(manifest.triangle_count));
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

        [Test]
        public void DoorPresentation_UsesOpposedInwardHingedLeaves()
        {
            GameObject prefab = CityBusResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                CityBusAssetRegistry registry =
                    instance.GetComponent<CityBusAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                CityBusPresentation presentation =
                    instance.AddComponent<CityBusPresentation>();
                presentation.Initialize(registry);

                Transform[] forwardLeaves =
                {
                    registry.FrontDoorForwardLeaf,
                    registry.RearDoorForwardLeaf
                };
                Transform[] rearwardLeaves =
                {
                    registry.FrontDoorRearwardLeaf,
                    registry.RearDoorRearwardLeaf
                };
                Quaternion[] forwardClosedRotations =
                {
                    forwardLeaves[0].rotation,
                    forwardLeaves[1].rotation
                };
                Quaternion[] rearwardClosedRotations =
                {
                    rearwardLeaves[0].rotation,
                    rearwardLeaves[1].rotation
                };
                Vector3[] forwardClosedPositions =
                {
                    forwardLeaves[0].position,
                    forwardLeaves[1].position
                };
                Vector3[] rearwardClosedPositions =
                {
                    rearwardLeaves[0].position,
                    rearwardLeaves[1].position
                };
                float[] forwardClosedLateralDistances =
                {
                    GetLateralDistance(instance.transform, forwardLeaves[0]),
                    GetLateralDistance(instance.transform, forwardLeaves[1])
                };
                float[] rearwardClosedLateralDistances =
                {
                    GetLateralDistance(instance.transform, rearwardLeaves[0]),
                    GetLateralDistance(instance.transform, rearwardLeaves[1])
                };
                Transform[] fixedPosts = registry.RendererBindings
                    .Where(binding => binding.Role == "door_post")
                    .Select(binding => binding.Renderer.transform)
                    .ToArray();
                Assert.That(fixedPosts, Has.Length.EqualTo(2));
                Assert.That(
                    fixedPosts.All(post => post.parent == registry.Body),
                    Is.True);
                Quaternion[] fixedPostClosedRotations =
                {
                    fixedPosts[0].rotation,
                    fixedPosts[1].rotation
                };
                Vector3[] fixedPostClosedPositions =
                {
                    fixedPosts[0].position,
                    fixedPosts[1].position
                };

                presentation.SetDoors(1f);

                for (int index = 0; index < forwardLeaves.Length; index++)
                {
                    float forwardAngle = AssertDoorLeafPose(
                        forwardLeaves[index],
                        forwardClosedRotations[index],
                        forwardClosedPositions[index],
                        instance.transform);
                    float rearwardAngle = AssertDoorLeafPose(
                        rearwardLeaves[index],
                        rearwardClosedRotations[index],
                        rearwardClosedPositions[index],
                        instance.transform);
                    Assert.That(
                        Mathf.Abs(forwardAngle),
                        Is.EqualTo(CityBusPresentation.MaximumDoorAngle)
                            .Within(0.01f));
                    Assert.That(
                        forwardAngle,
                        Is.EqualTo(-rearwardAngle).Within(0.01f),
                        "Each doorway must open its leaves in opposite " +
                        "directions.");
                    Assert.That(
                        GetLateralDistance(
                            instance.transform,
                            forwardLeaves[index]),
                        Is.LessThan(
                            forwardClosedLateralDistances[index] - 0.10f),
                        "The forward leaf must fold into the cabin.");
                    Assert.That(
                        GetLateralDistance(
                            instance.transform,
                            rearwardLeaves[index]),
                        Is.LessThan(
                            rearwardClosedLateralDistances[index] - 0.10f),
                        "The rearward leaf must fold into the cabin.");
                }

                for (int index = 0; index < fixedPosts.Length; index++)
                {
                    Assert.That(
                        fixedPosts[index].position,
                        Is.EqualTo(fixedPostClosedPositions[index]));
                    Assert.That(
                        Quaternion.Angle(
                            fixedPosts[index].rotation,
                            fixedPostClosedRotations[index]),
                        Is.LessThan(0.001f));
                }

                presentation.ResetForPool();
                for (int index = 0; index < forwardLeaves.Length; index++)
                {
                    Assert.That(
                        Quaternion.Angle(
                            forwardLeaves[index].rotation,
                            forwardClosedRotations[index]),
                        Is.LessThan(0.001f));
                    Assert.That(
                        Quaternion.Angle(
                            rearwardLeaves[index].rotation,
                            rearwardClosedRotations[index]),
                        Is.LessThan(0.001f));
                }

                Quaternion neutralSuspensionRotation =
                    presentation.SuspensionVisual.rotation;
                presentation.SetMotion(
                    0.67f,
                    CityBusActor.CruiseSpeed,
                    -CityBusActor.ServiceDeceleration,
                    14f,
                    true,
                    0.2f);
                Assert.That(
                    Quaternion.Angle(
                        presentation.SuspensionVisual.rotation,
                        neutralSuspensionRotation),
                    Is.GreaterThan(0.05f));
                Quaternion suspensionDelta =
                    presentation.SuspensionVisual.rotation *
                    Quaternion.Inverse(neutralSuspensionRotation);
                Vector3 sprungHingeAxis =
                    suspensionDelta * instance.transform.up;
                Vector3 sprungReference =
                    suspensionDelta * instance.transform.forward;
                Quaternion[] sprungForwardClosedRotations =
                {
                    forwardLeaves[0].rotation,
                    forwardLeaves[1].rotation
                };
                Quaternion[] sprungRearwardClosedRotations =
                {
                    rearwardLeaves[0].rotation,
                    rearwardLeaves[1].rotation
                };
                Vector3[] sprungForwardClosedPositions =
                {
                    forwardLeaves[0].position,
                    forwardLeaves[1].position
                };
                Vector3[] sprungRearwardClosedPositions =
                {
                    rearwardLeaves[0].position,
                    rearwardLeaves[1].position
                };

                presentation.SetDoors(1f);

                for (int index = 0; index < forwardLeaves.Length; index++)
                {
                    float forwardAngle = AssertDoorLeafPose(
                        forwardLeaves[index],
                        sprungForwardClosedRotations[index],
                        sprungForwardClosedPositions[index],
                        sprungHingeAxis,
                        sprungReference);
                    float rearwardAngle = AssertDoorLeafPose(
                        rearwardLeaves[index],
                        sprungRearwardClosedRotations[index],
                        sprungRearwardClosedPositions[index],
                        sprungHingeAxis,
                        sprungReference);
                    Assert.That(
                        Mathf.Abs(forwardAngle),
                        Is.EqualTo(CityBusPresentation.MaximumDoorAngle)
                            .Within(0.01f));
                    Assert.That(
                        forwardAngle,
                        Is.EqualTo(-rearwardAngle).Within(0.01f));
                }

                presentation.ResetForPool();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SuspensionPresentation_UsesBusVerticalAndBodyAxes()
        {
            GameObject prefab = CityBusResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                CityBusAssetRegistry registry =
                    instance.GetComponent<CityBusAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                CityBusPresentation presentation =
                    instance.AddComponent<CityBusPresentation>();
                presentation.Initialize(registry);
                Transform suspension = presentation.SuspensionVisual;
                Assert.That(suspension, Is.Not.Null);
                Vector3 neutralPosition = suspension.position;
                Quaternion neutralRotation = suspension.rotation;

                presentation.SetMotion(
                    0.67f,
                    CityBusActor.CruiseSpeed,
                    0f,
                    0f,
                    false,
                    1f);

                Vector3 displacement =
                    suspension.position - neutralPosition;
                float verticalDisplacement = Vector3.Dot(
                    displacement,
                    instance.transform.up);
                float longitudinalDisplacement = Mathf.Abs(Vector3.Dot(
                    displacement,
                    instance.transform.forward));
                float lateralDisplacement = Mathf.Abs(Vector3.Dot(
                    displacement,
                    instance.transform.right));
                Assert.That(
                    Mathf.Abs(presentation.SuspensionHeave),
                    Is.GreaterThan(0.005f));
                Assert.That(
                    verticalDisplacement,
                    Is.EqualTo(presentation.SuspensionHeave)
                        .Within(0.0001f),
                    "Suspension heave must use the bus vertical rather " +
                    "than the imported FBX body's longitudinal axis.");
                Assert.That(
                    longitudinalDisplacement,
                    Is.LessThan(0.0001f));
                Assert.That(
                    lateralDisplacement,
                    Is.LessThan(0.0001f));

                Quaternion neutralRotationInPresentation =
                    Quaternion.Inverse(instance.transform.rotation) *
                    neutralRotation;
                Quaternion expectedRotation =
                    instance.transform.rotation *
                    Quaternion.Euler(
                        presentation.SuspensionPitch,
                        0f,
                        presentation.SuspensionRoll) *
                    neutralRotationInPresentation;
                Assert.That(
                    Quaternion.Angle(
                        suspension.rotation,
                        expectedRotation),
                    Is.LessThan(0.001f),
                    "Pitch and roll must also use the bus presentation " +
                    "axes rather than imported FBX-local axes.");

                presentation.ResetForPool();
                Assert.That(suspension.position, Is.EqualTo(neutralPosition));
                Assert.That(
                    Quaternion.Angle(
                        suspension.rotation,
                        neutralRotation),
                    Is.LessThan(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static float AssertDoorLeafPose(
            Transform leaf,
            Quaternion closedRotation,
            Vector3 closedPosition,
            Transform busRoot)
        {
            return AssertDoorLeafPose(
                leaf,
                closedRotation,
                closedPosition,
                busRoot.up,
                busRoot.forward);
        }

        private static float AssertDoorLeafPose(
            Transform leaf,
            Quaternion closedRotation,
            Vector3 closedPosition,
            Vector3 hingeAxis,
            Vector3 reference)
        {
            Quaternion worldDelta =
                leaf.rotation * Quaternion.Inverse(closedRotation);
            Assert.That(
                Vector3.Angle(
                    worldDelta * hingeAxis,
                    hingeAxis),
                Is.LessThan(0.01f),
                "Each imported leaf must rotate around the bus's " +
                "vertical axis.");
            Assert.That(
                Vector3.Distance(leaf.position, closedPosition),
                Is.LessThan(0.0001f),
                "Opening a leaf must not move its hinge pivot.");

            return Vector3.SignedAngle(
                reference,
                worldDelta * reference,
                hingeAxis);
        }

        private static float GetLateralDistance(
            Transform busRoot,
            Transform leaf)
        {
            Renderer[] renderers =
                leaf.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return Mathf.Abs(
                busRoot.InverseTransformPoint(bounds.center).x);
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
