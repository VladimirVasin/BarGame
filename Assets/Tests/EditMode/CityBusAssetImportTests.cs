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
            Assert.That(manifest.pivots, Is.Not.Null.And.Not.Empty);
            Assert.That(manifest.triangle_count, Is.InRange(900, 12000));
            Assert.That(manifest.passenger_seat_count, Is.GreaterThanOrEqualTo(10));
            Assert.That(manifest.dimensions_m.length, Is.EqualTo(8.25f));
            Assert.That(manifest.dimensions_m.width, Is.EqualTo(2.38f));
            Assert.That(manifest.dimensions_m.height, Is.EqualTo(2.95f));
            Assert.That(manifest.dimensions_m.wheelbase, Is.EqualTo(4.50f));
            Assert.That(manifest.dimensions_m.wheel_radius, Is.EqualTo(0.43f));

            CityBusPivotManifest steeringSource = manifest.pivots.Single(
                pivot => pivot.name == "PIVOT_SteeringWheel");
            Assert.That(steeringSource.role, Is.EqualTo("steering_wheel"));
            Assert.That(steeringSource.parent, Is.EqualTo("ROOT_Body"));
            Assert.That(steeringSource.runtime_axis_local, Is.EqualTo("+Z"));
            Assert.That(steeringSource.travel_m, Is.Zero);
            AssertSourceVector(
                steeringSource.local_position,
                new Vector3(0.60f, -3.32f, 1.57f));
            AssertSourceVector(
                steeringSource.local_rotation_degrees,
                new Vector3(-90f, 0f, 0f));
            AssertPivotSource(
                manifest,
                "ANCHOR_SteeringGrip.L",
                "left_steering_grip",
                "PIVOT_SteeringWheel");
            AssertPivotSource(
                manifest,
                "ANCHOR_SteeringGrip.R",
                "right_steering_grip",
                "PIVOT_SteeringWheel");

            CityBusPivotManifest buttonSource = AssertPivotSource(
                manifest,
                "PIVOT_DoorButton",
                "door_button",
                "ROOT_Body");
            Assert.That(buttonSource.runtime_axis_local, Is.EqualTo("+Y"));
            Assert.That(buttonSource.travel_m, Is.EqualTo(0.012f));
            AssertSourceVector(
                buttonSource.local_position,
                new Vector3(0.30f, -3.335f, 1.50f));
            AssertPivotSource(
                manifest,
                "ANCHOR_DoorButtonPress",
                "door_button_press",
                "PIVOT_DoorButton");
            CityBusPivotManifest lookSource = AssertPivotSource(
                manifest,
                "ANCHOR_DriverDoorLook",
                "driver_door_look",
                "ROOT_Body");
            AssertSourceVector(
                lookSource.local_position,
                new Vector3(-0.90f, -3.05f, 2.12f));

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
                Assert.That(registry.SteeringWheelPivot, Is.Not.Null);
                Assert.That(registry.LeftSteeringGrip, Is.Not.Null);
                Assert.That(registry.RightSteeringGrip, Is.Not.Null);
                Assert.That(registry.DoorButtonPivot, Is.Not.Null);
                Assert.That(registry.DoorButtonPressAnchor, Is.Not.Null);
                Assert.That(registry.DriverDoorLookAnchor, Is.Not.Null);
                Assert.That(
                    registry.SteeringWheelPivot.parent,
                    Is.SameAs(registry.Body));
                Assert.That(
                    registry.LeftSteeringGrip.parent,
                    Is.SameAs(registry.SteeringWheelPivot));
                Assert.That(
                    registry.RightSteeringGrip.parent,
                    Is.SameAs(registry.SteeringWheelPivot));
                Assert.That(
                    registry.DoorButtonPivot.parent,
                    Is.SameAs(registry.Body));
                Assert.That(
                    registry.DoorButtonPressAnchor.parent,
                    Is.SameAs(registry.DoorButtonPivot));
                Assert.That(
                    registry.DriverDoorLookAnchor.parent,
                    Is.SameAs(registry.Body));
                Assert.That(
                    registry.SteeringWheelAxisLocal,
                    Is.EqualTo(Vector3.forward));
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
                CityBusRendererBinding steeringWheel =
                    registry.RendererBindings.Single(
                        binding =>
                            binding.SourceName == "INT_SteeringWheel");
                CityBusRendererBinding doorButton =
                    registry.RendererBindings.Single(
                        binding => binding.SourceName == "INT_DoorButton");
                Assert.That(
                    steeringWheel.Renderer.transform.parent,
                    Is.SameAs(registry.SteeringWheelPivot));
                Assert.That(
                    doorButton.Renderer.transform.parent,
                    Is.SameAs(registry.DoorButtonPivot));
                AssertGripOnRim(
                    registry.SteeringWheelPivot,
                    registry.LeftSteeringGrip,
                    registry.SteeringWheelAxisLocal);
                AssertGripOnRim(
                    registry.SteeringWheelPivot,
                    registry.RightSteeringGrip,
                    registry.SteeringWheelAxisLocal);
                Vector3 buttonTravelWorld =
                    registry.DoorButtonPivot.parent.TransformVector(
                        registry.DoorButtonTravelLocal);
                Vector3 buttonFaceOffset =
                    registry.DoorButtonPressAnchor.position -
                    registry.DoorButtonPivot.position;
                Assert.That(
                    buttonTravelWorld.magnitude,
                    Is.EqualTo(0.012f).Within(0.0001f));
                Assert.That(
                    buttonFaceOffset.magnitude,
                    Is.EqualTo(0.0265f).Within(0.001f));
                Assert.That(
                    Vector3.Dot(
                        buttonTravelWorld.normalized,
                        -buttonFaceOffset.normalized),
                    Is.GreaterThan(0.999f));
                Assert.That(
                    instance.transform.InverseTransformPoint(
                        registry.DriverDoorLookAnchor.position).y,
                    Is.GreaterThan(
                        instance.transform.InverseTransformPoint(
                            registry.DriverSeatAnchor.position).y + 0.8f));

                Quaternion steeringRest =
                    registry.SteeringWheelPivot.localRotation;
                Vector3 buttonRest = registry.DoorButtonPivot.localPosition;
                Vector3 buttonWorldRest = registry.DoorButtonPivot.position;
                Quaternion buttonRotationRest =
                    registry.DoorButtonPivot.localRotation;
                registry.SteeringWheelPivot.localRotation =
                    steeringRest * Quaternion.AngleAxis(
                        47f,
                        registry.SteeringWheelAxisLocal);
                registry.DoorButtonPivot.localPosition =
                    buttonRest + registry.DoorButtonTravelLocal;
                Assert.That(
                    Vector3.Distance(
                        registry.DoorButtonPivot.position,
                        buttonWorldRest),
                    Is.EqualTo(0.012f).Within(0.0001f));
                registry.DoorButtonPivot.localRotation *=
                    Quaternion.AngleAxis(8f, Vector3.right);
                registry.ResetArticulation();
                Assert.That(
                    Quaternion.Angle(
                        registry.SteeringWheelPivot.localRotation,
                        steeringRest),
                    Is.LessThan(0.001f));
                Assert.That(
                    registry.DoorButtonPivot.localPosition,
                    Is.EqualTo(buttonRest));
                Assert.That(
                    Quaternion.Angle(
                        registry.DoorButtonPivot.localRotation,
                        buttonRotationRest),
                    Is.LessThan(0.001f));
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

        private static CityBusPivotManifest AssertPivotSource(
            CityBusManifest manifest,
            string name,
            string role,
            string parent)
        {
            CityBusPivotManifest pivot = manifest.pivots.Single(
                candidate => candidate.name == name);
            Assert.That(pivot.role, Is.EqualTo(role));
            Assert.That(pivot.parent, Is.EqualTo(parent));
            Assert.That(pivot.local_position, Has.Length.EqualTo(3));
            Assert.That(
                pivot.local_rotation_degrees,
                Has.Length.EqualTo(3));
            return pivot;
        }

        private static void AssertSourceVector(
            float[] actual,
            Vector3 expected)
        {
            Assert.That(actual, Has.Length.EqualTo(3));
            Assert.That(actual[0], Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual[1], Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual[2], Is.EqualTo(expected.z).Within(0.0001f));
        }

        private static void AssertGripOnRim(
            Transform steeringWheel,
            Transform grip,
            Vector3 axisLocal)
        {
            Vector3 axis = steeringWheel.TransformDirection(
                axisLocal).normalized;
            Vector3 offset = grip.position - steeringWheel.position;
            Vector3 radial = offset - Vector3.Project(
                offset,
                axis);
            Assert.That(radial.magnitude, Is.EqualTo(0.18f).Within(0.001f));
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
        public void DriverPresentation_TracksWheelPressesButtonAndLooksAtDoor()
        {
            GameObject busPrefab = CityBusResources.LoadPrefab();
            GameObject driverPrefab = CityBusDriverResources.LoadPrefab();
            Assert.That(busPrefab, Is.Not.Null);
            Assert.That(driverPrefab, Is.Not.Null);
            GameObject bus = UnityEngine.Object.Instantiate(busPrefab);
            GameObject driver = UnityEngine.Object.Instantiate(
                driverPrefab,
                bus.transform,
                false);
            try
            {
                CityBusAssetRegistry busRegistry =
                    bus.GetComponent<CityBusAssetRegistry>();
                CityBusDriverAssetRegistry driverRegistry =
                    driver.GetComponent<CityBusDriverAssetRegistry>();
                Assert.That(busRegistry, Is.Not.Null);
                Assert.That(driverRegistry, Is.Not.Null);

                CityBusPresentation presentation =
                    bus.AddComponent<CityBusPresentation>();
                presentation.Initialize(busRegistry);
                presentation.AttachDriver(driverRegistry);
                CityBusDriverPresentation driverPresentation =
                    presentation.DriverPresentation;
                Assert.That(driverPresentation, Is.Not.Null);

                presentation.SetDriverDoorSample(default);
                presentation.SetMotion(
                    0f,
                    3f,
                    0f,
                    20f,
                    false,
                    1f / 60f);

                Assert.That(
                    Mathf.Abs(presentation.SteeringWheelAngle),
                    Is.GreaterThan(60f));
                Assert.That(
                    driverPresentation.LeftGripDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));
                Assert.That(
                    driverPresentation.RightGripDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));

                CityBusDriverDoorSample openingContact =
                    CityBusDriverDoorTimeline.SampleDwell(
                        0f,
                        CityBusActor.DwellDuration,
                        CityBusActor.DoorTransitionDuration);
                presentation.SetDriverDoorSample(openingContact);
                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    true,
                    1f / 60f);

                Assert.That(
                    presentation.DoorButtonPressFactor,
                    Is.EqualTo(1f));
                Assert.That(
                    driverPresentation.RightHandButtonDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));
                Assert.That(
                    driverPresentation.LeftGripDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));
                Quaternion neutralHeadRotation =
                    driverRegistry.Head.rotation;
                Vector3 neutralFaceDirection =
                    ResolveDriverFaceDirection(driverRegistry, bus.transform.up);

                CityBusDriverDoorSample openingLook =
                    CityBusDriverDoorTimeline.SampleDwell(
                        CityBusActor.DwellDuration * 0.5f,
                        CityBusActor.DwellDuration,
                        CityBusActor.DoorTransitionDuration);
                presentation.SetDriverDoorSample(openingLook);
                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    true,
                    1f / 60f);
                Assert.That(
                    driverPresentation.DoorLookWeight,
                    Is.EqualTo(1f).Within(0.0001f));
                Vector3 doorDirection = Vector3.ProjectOnPlane(
                    busRegistry.DriverDoorLookAnchor.position -
                    driverRegistry.Head.position,
                    bus.transform.up).normalized;
                Vector3 turnedFaceDirection =
                    ResolveDriverFaceDirection(driverRegistry, bus.transform.up);
                float neutralDoorAlignment = Vector3.Dot(
                    neutralFaceDirection,
                    doorDirection);
                float turnedDoorAlignment = Vector3.Dot(
                    turnedFaceDirection,
                    doorDirection);
                Assert.That(
                    Quaternion.Angle(
                        neutralHeadRotation,
                        driverRegistry.Head.rotation),
                    Is.GreaterThan(55f),
                    "The visible head bone must make a readable door turn.");
                Assert.That(
                    turnedDoorAlignment,
                    Is.GreaterThan(neutralDoorAlignment + 0.35f),
                    "The actual face direction must turn toward the door.");
                Assert.That(
                    turnedDoorAlignment,
                    Is.GreaterThan(0.90f),
                    "The driver's visible face must point toward the door.");
                Assert.That(
                    driverPresentation.DoorLookAlignment,
                    Is.EqualTo(turnedDoorAlignment).Within(0.001f));

                Transform focusRoot = new GameObject(
                    "Driver Focus Test Player").transform;
                focusRoot.SetParent(bus.transform, true);
                Vector3 doorOutward = Vector3.ProjectOnPlane(
                    busRegistry.FrontDoorEntryAnchor.position -
                    busRegistry.DriverSeatAnchor.position,
                    bus.transform.up).normalized;
                focusRoot.position =
                    busRegistry.FrontDoorEntryAnchor.position +
                    doorOutward * 1.2f;
                presentation.SetDriverFocusTarget(focusRoot);
                float neutralNeckLength = Vector3.Distance(
                    driverRegistry.Neck.position,
                    driverRegistry.Head.position);
                Vector3 neutralHeadPosition = driverRegistry.Head.position;
                Vector3 neutralNeckScale = driverRegistry.Neck.localScale;

                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    true,
                    0.5f);

                float stretchedNeckRatio = Vector3.Distance(
                        driverRegistry.Neck.position,
                        driverRegistry.Head.position) /
                    neutralNeckLength;
                Vector3 playerDirection = Vector3.ProjectOnPlane(
                    driverPresentation.PlayerFocusPoint -
                    driverRegistry.Head.position,
                    bus.transform.up).normalized;
                float playerAlignment = Vector3.Dot(
                    ResolveDriverFaceDirection(
                        driverRegistry,
                        bus.transform.up),
                    playerDirection);
                Assert.That(
                    driverPresentation.IsPlayerNearFrontDoor,
                    Is.True);
                Assert.That(
                    driverPresentation.PlayerFocusWeight,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    playerAlignment,
                    Is.GreaterThan(0.90f));
                Assert.That(
                    stretchedNeckRatio,
                    Is.InRange(
                        1.10f,
                        CityBusDriverPresentation
                            .MaximumNeckStretchRatio + 0.001f));
                Assert.That(
                    Vector3.Distance(
                        neutralHeadPosition,
                        driverRegistry.Head.position),
                    Is.GreaterThan(0.05f));
                Assert.That(
                    driverPresentation.LeftGripDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));
                Assert.That(
                    driverPresentation.RightGripDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));

                focusRoot.position =
                    busRegistry.FrontDoorEntryAnchor.position +
                    doorOutward *
                    (CityBusDriverPresentation.PlayerFocusZeroDistance + 1f);
                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    true,
                    0.7f);
                Assert.That(
                    driverPresentation.IsPlayerNearFrontDoor,
                    Is.False);
                Assert.That(driverPresentation.PlayerFocusWeight, Is.Zero);
                Assert.That(
                    Vector3.Distance(
                        driverRegistry.Neck.position,
                        driverRegistry.Head.position),
                    Is.EqualTo(neutralNeckLength).Within(0.001f));
                Assert.That(
                    driverRegistry.Neck.localScale,
                    Is.EqualTo(neutralNeckScale));

                float closingStart =
                    CityBusActor.DwellDuration -
                    CityBusActor.DoorTransitionDuration;
                CityBusDriverDoorSample closingContact =
                    CityBusDriverDoorTimeline.SampleDwell(
                        closingStart,
                        CityBusActor.DwellDuration,
                        CityBusActor.DoorTransitionDuration);
                presentation.SetDriverDoorSample(closingContact);
                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    true,
                    1f / 60f);
                Assert.That(
                    driverPresentation.RightHandButtonDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));

                Renderer[] blinkingEyes = driverRegistry.RendererBindings
                    .Where(binding =>
                        binding.Role == "long_horizontal_eye" ||
                        binding.Role == "visible_eye_pupil")
                    .Select(binding => binding.Renderer)
                    .ToArray();
                Assert.That(blinkingEyes, Has.Length.EqualTo(4));

                presentation.ResetForPool();
                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    false,
                    CityBusDriverPresentation.BlinkStartTime +
                    CityBusDriverPresentation.BlinkCloseDuration +
                    CityBusDriverPresentation.BlinkHoldDuration * 0.5f);
                Assert.That(driverPresentation.EyesClosed, Is.True);
                Assert.That(driverPresentation.BlinkClosure, Is.EqualTo(1f));
                Assert.That(
                    blinkingEyes.All(renderer => renderer.forceRenderingOff),
                    Is.True);

                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    false,
                    CityBusDriverPresentation.BlinkHoldDuration +
                    CityBusDriverPresentation.BlinkOpenDuration);
                Assert.That(driverPresentation.EyesClosed, Is.False);
                Assert.That(driverPresentation.BlinkClosure, Is.Zero);
                Assert.That(
                    blinkingEyes.All(renderer => !renderer.forceRenderingOff),
                    Is.True);

                presentation.ResetForPool();
                Assert.That(presentation.SteeringWheelAngle, Is.Zero);
                Assert.That(presentation.DoorButtonPressFactor, Is.Zero);
                Assert.That(driverPresentation.RightHandButtonBlend, Is.Zero);
                Assert.That(driverPresentation.DoorLookWeight, Is.Zero);
                Assert.That(driverPresentation.PlayerFocusWeight, Is.Zero);
                Assert.That(driverPresentation.FocusStretchDistance, Is.Zero);
                Assert.That(driverPresentation.NeckStretchRatio, Is.EqualTo(1f));
                Assert.That(driverPresentation.BlinkClosure, Is.Zero);
                Assert.That(driverPresentation.EyesClosed, Is.False);
                Assert.That(
                    blinkingEyes.All(renderer => !renderer.forceRenderingOff),
                    Is.True);
                Assert.That(
                    driverPresentation.LeftGripDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));
                Assert.That(
                    driverPresentation.RightGripDistance,
                    Is.LessThanOrEqualTo(
                        CityBusDriverPresentation.MaximumGripError));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bus);
            }
        }

        private static Vector3 ResolveDriverFaceDirection(
            CityBusDriverAssetRegistry registry,
            Vector3 up)
        {
            Vector3 eyeCenter =
                (registry.FaceEyeLeft.position +
                 registry.FaceEyeRight.position) * 0.5f;
            return Vector3.ProjectOnPlane(
                    eyeCenter - registry.Head.position,
                    up)
                .normalized;
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
            public CityBusPivotManifest[] pivots;
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

        [Serializable]
        private sealed class CityBusPivotManifest
        {
            public string name;
            public string role;
            public string parent;
            public float[] local_position;
            public float[] local_rotation_degrees;
            public string runtime_axis_local;
            public float travel_m;
        }
    }
}
