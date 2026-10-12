using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class SecondaryMotionBakePlayModeTests
    {
        private static void VerifySlingBakedPlayback(Player3DAssetRegistry registry, CaptureCamera camera, string pictures)
        {
            Assert.That(registry.TryGetAnimation("Idle", out Player3DAnimationBinding binding), Is.True);
            AnimationClip clip = binding.Clip;
            TextAsset asset = Resources.Load<TextAsset>(SecondaryMotionBakeRuntime.ResourceRoot + "stored-shotgun/" + clip.name);
            Assert.That(asset, Is.Not.Null, "The proof consumes the offline physical recording.");
            Vector3 position = registry.transform.position;
            Quaternion rotation = registry.transform.rotation;
            PlayerEquipmentContacts originalEquipment = registry.GetComponent<PlayerEquipmentContacts>();
            CombatShotgunSling sling = null;
            try
            {
                clip.SampleAnimation(registry.Animator.gameObject, clip.length * .5f);
                registry.GetComponent<CharacterJointDeformation>()?.ApplyPose();
                registry.GetComponent<PlayerBootDeformation>()?.ApplyPose(FootGroundSample.None, FootGroundSample.None);
                sling = CombatShotgunSling.CreateBakeSling(registry);
                sling.enabled = false;
                // The camera predates the owned gun and mount; their imported
                // descendants must join the rig's existing capture layer.
                foreach (Transform part in registry.GetComponentsInChildren<Transform>(true))
                    part.gameObject.layer = registry.gameObject.layer;
                var decode = typeof(SecondaryMotionBakeRuntime).GetMethod("Decode",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var recorded = (SecondaryMotionBakeRuntime.ClipData)decode.Invoke(null, new object[] { asset.bytes });
                Assert.That(recorded, Is.Not.Null, "The proof requires a compatible recorded bank header.");
                SecondaryMotionBakeRuntime.Track slingTrack = null;
                foreach (SecondaryMotionBakeRuntime.Track track in recorded.Tracks)
                    if (track.Part == SecondaryMotionBakePart.Sling) slingTrack = track;
                Assert.That(slingTrack, Is.Not.Null, "The stored-shotgun bank must contain its physical sling track.");
                Assert.That(sling.BakedPoseSignature, Is.EqualTo(slingTrack.Signature),
                    "The authored sling signature must be independent of its creation pose; recorded=" + slingTrack.Signature +
                    "; current=" + sling.BakedPoseSignature);
                using (sling.BeginBakeCapture("stored-shotgun"))
                {
                    VerifyAtRootPose();
                    registry.transform.SetPositionAndRotation(position + new Vector3(2f, .4f, -1f),
                        Quaternion.AngleAxis(63f, Vector3.up) * rotation);
                    VerifyAtRootPose();
                    Assert.That(sling.BakedPlaybackFrames, Is.EqualTo(2));
                    Assert.That(sling.TotalSimulationSteps, Is.Zero, "Preview must never advance the live solver.");
                    Assert.That(sling.ProjectionWorkspaceAllocations, Is.Zero);
                    Assert.That(sling.SteadyProjectionIterations, Is.Zero);
                    Assert.That(sling.SteadyLengthRepairIterations, Is.Zero);
                    VerifyIdleLoopSeam();
                    clip.SampleAnimation(registry.Animator.gameObject, clip.length * .5f);
                    registry.GetComponent<CharacterJointDeformation>()?.ApplyPose();
                    registry.GetComponent<PlayerBootDeformation>()?.ApplyPose(FootGroundSample.None, FootGroundSample.None);
                    VerifyWarmEntry();
                }
                Assert.That(sling.BakedPoseValueCount, Is.EqualTo(138));
                Assert.That(sling.BakedPoseSignature, Does.StartWith("shotgun-sling-physical-v1-"));

                void VerifyAtRootPose()
                {
                    SecondaryMotionBakeRuntime.SetContext(registry, clip, clip.length * .5f, "stored-shotgun");
                    Assert.That(SecondaryMotionBakeRuntime.TrySample(registry, SecondaryMotionBakePart.Sling,
                        sling.BakedPoseSignature, sling.BakedPoseValueCount, false, new WindSample(0f, 0f),
                        out SecondaryMotionBakeSample sample, "stored-shotgun"), Is.True,
                        "Shared sling context must match before checking its anchors: " +
                        SecondaryMotionBakeRuntime.DescribeContext(registry, SecondaryMotionBakePart.Sling));
                    bool presented = sling.PreviewBakedPose(.5f);
                    Assert.That(presented, Is.True, "The final posed rig should accept its physical sling recording. " +
                        SecondaryMotionBakeRuntime.DescribeContext(registry, SecondaryMotionBakePart.Sling) + "; " + AnchorDiagnostics());
                    for (int point = 0; point < 41; point++)
                    {
                        int offset = point * 3;
                        Vector3 local = new Vector3(Value(offset), Value(offset + 1), Value(offset + 2));
                        Assert.That(Vector3.Distance(sling.WorldCenterlinePoint(point * 2), registry.transform.TransformPoint(local)),
                            Is.LessThan(.002f), "Recorded physical nodes must use the live world root matrix.");
                    }
                    PlayerEquipmentContacts equipment = registry.GetComponent<PlayerEquipmentContacts>();
                    equipment.UpdatePose();
                    int count = equipment.BoxSnapshotCount(false);
                    Assert.That(count, Is.GreaterThan(0), "The actual shotgun mesh remains registered as world equipment.");
                    Assert.That(equipment.BoxSnapshotCount(true), Is.GreaterThan(count), "Playback must publish the displayed ribbon contacts.");
                    using var boxes = new NativeArray<PlayerEquipmentContacts.BoxSnapshot>(count, Allocator.Temp);
                    Assert.That(equipment.CopyBoxSnapshots(boxes, includeRibbons: false), Is.EqualTo(count));
                    bool foundLiveMesh = false;
                    foreach (MeshFilter mesh in sling.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mesh.sharedMesh == null || mesh.name == CombatShotgunSling.RibbonName) continue;
                        Bounds authored = mesh.sharedMesh.bounds;
                        for (int box = 0; box < count; box++)
                        {
                            PlayerEquipmentContacts.BoxSnapshot native = boxes[box];
                            // Imported parts can share a transform matrix while
                            // retaining different local mesh bounds. Match the
                            // complete query box, including its native padding.
                            if (!native.ToWorld.Equals(mesh.transform.localToWorldMatrix) ||
                                !native.LocalCentre.Equals(authored.center) ||
                                !native.LocalExtent.Equals(authored.extents + native.Padding)) continue;
                            Assert.That(Vector3.Distance(native.ToWorld.MultiplyPoint3x4(native.LocalCentre),
                                mesh.transform.TransformPoint(authored.center)), Is.LessThan(.0001f));
                            Assert.That(native.WorldMinimum.x, Is.LessThanOrEqualTo(native.WorldMaximum.x));
                            foundLiveMesh = true;
                        }
                    }
                    Assert.That(foundLiveMesh, Is.True, "Native equipment snapshots must contain the current authored mesh transform.");
                    float Value(int index) => Mathf.LerpUnclamped(sample.From[index], sample.To[index], sample.Alpha);

                    string AnchorDiagnostics()
                    {
                        Vector3 stock = Vector3.zero, foreend = Vector3.zero;
                        foreach (Transform anchor in sling.GetComponentsInChildren<Transform>(true))
                        {
                            if (anchor.name == "SlingStock") stock = anchor.position;
                            else if (anchor.name == "SlingForeend") foreend = anchor.position;
                        }
                        Transform left = CityPedestrianHandProps.FindSocket(registry.Animator.transform, "upper_arm.L");
                        Transform right = CityPedestrianHandProps.FindSocket(registry.Animator.transform, "upper_arm.R");
                        Vector3 up = (registry.Anchors.Head.position - registry.Anchors.Chest.position).normalized;
                        Vector3 across = (right.position - left.position).normalized;
                        Vector3 shoulder = left.position + up * .07f - across * .012f + Vector3.Cross(across, up).normalized * .04f;
                        float length = 0f, error = 0f;
                        for (int point = 1; point <= 40; point++)
                        {
                            float link = Vector3.Distance(Recorded(point * 3), Recorded((point - 1) * 3));
                            length += link; error = Mathf.Max(error, Mathf.Abs(link - sling.AuthoredLength / 40f));
                        }
                        return "anchor residuals stock/foreend/shoulder/hand=" +
                            $"{F(Vector3.Distance(stock, Recorded(123)))}/{F(Vector3.Distance(foreend, Recorded(126)))}/" +
                            $"{F(Vector3.Distance(shoulder, Recorded(129)))}/{F(Vector3.Distance(sling.HandTargetWorld, Recorded(132)))}m; " +
                            $"recorded shoulder indices={sample.From[135]}/{sample.To[135]}, decision={sling.LastBakedDecision}; " +
                            $"recorded/live shoulder amount={F(Value(136))}/{F(sling.ShoulderAttachmentAmount)}, " +
                            $"hand amount={F(Value(137))}/{F(sling.LeftHandAttachmentAmount)}; recorded length/link error={F(length)}/{F(error)}m; " +
                            $"pure calls={sling.BakedPlaybackFrames}, physical steps={sling.TotalSimulationSteps}";

                        Vector3 Recorded(int offset) => registry.transform.TransformPoint(new Vector3(Value(offset), Value(offset + 1), Value(offset + 2)));
                        string F(float value) => value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                void VerifyGeometry(PlayerScarfBodyContacts contacts)
                {
                    float[] pose = new float[sling.BakedPoseValueCount];
                    sling.CaptureBakedPose(pose);
                    int shoulderPoint = Mathf.RoundToInt(pose[135]);
                    Vector3[] nodes = Nodes();
                    bool foundStock = false, foundForeend = false;
                    foreach (Transform anchor in sling.GetComponentsInChildren<Transform>(true))
                    {
                        if (anchor.name == "SlingStock")
                        { foundStock = true; Assert.That(Vector3.Distance(nodes[0], anchor.position), Is.LessThan(.00001f)); }
                        else if (anchor.name == "SlingForeend")
                        { foundForeend = true; Assert.That(Vector3.Distance(nodes[40], anchor.position), Is.LessThan(.00001f)); }
                    }
                    Assert.That(foundStock && foundForeend, Is.True, "The proof must measure both real authored swivel anchors.");
                    Transform left = CityPedestrianHandProps.FindSocket(registry.Animator.transform, "upper_arm.L");
                    Transform right = CityPedestrianHandProps.FindSocket(registry.Animator.transform, "upper_arm.R");
                    Vector3 up = (registry.Anchors.Head.position - registry.Anchors.Chest.position).normalized;
                    Vector3 across = (right.position - left.position).normalized;
                    Vector3 forward = Vector3.Cross(across, up).normalized;
                    Vector3 shoulder = left.position + up * .07f - across * .012f + forward * .04f;
                    Assert.That(Vector3.Distance(nodes[shoulderPoint], shoulder), Is.LessThan(.00001f),
                        "The displayed chain must retain its actual hard shoulder pin throughout entry.");
                    float length = 0f;
                    using var queries = contacts.BeginNativeQueries();
                    for (int point = 0; point <= 40; point++)
                    {
                        if (point > 0)
                        {
                            float link = Vector3.Distance(nodes[point - 1], nodes[point]);
                            length += link;
                            Assert.That(Mathf.Abs(link - sling.AuthoredLength / 40f), Is.LessThan(.0005f));
                        }
                        if (point == 0 || point == 40 || point == shoulderPoint) continue;
                        Vector3 resolved = nodes[point];
                        Vector3 outward = Vector3.ProjectOnPlane(resolved - registry.Anchors.Chest.position, up);
                        contacts.ResolvePointOutward(ref resolved, outward.sqrMagnitude > .0004f ? outward.normalized : across,
                            querySlot: point);
                        Assert.That(Vector3.Distance(nodes[point], resolved), Is.LessThan(.0015f),
                            "The rendered free chain must stay outside the live native body support during entry and exit.");
                    }
                    Assert.That(Mathf.Abs(length - sling.AuthoredLength), Is.LessThan(.0061f));
                }

                Vector3[] Nodes()
                {
                    var result = new Vector3[41];
                    for (int point = 0; point < result.Length; point++) result[point] = sling.WorldCenterlinePoint(point * 2);
                    return result;
                }

                void VerifyIdleLoopSeam()
                {
                    registry.transform.SetPositionAndRotation(position, rotation);
                    using var contacts = new PlayerScarfBodyContacts(registry, useMeshSupportPlanes: true,
                        includeEquipment: false, poseSourceSkin: true, pointQuerySlots: 41, jacketSurfacePadding: .02025f);
                    long physicalSteps = sling.TotalSimulationSteps;
                    PresentAt(Mathf.Max(0f, clip.length - .00001f));
                    Vector3[] end = Nodes();
                    PresentAt(0f);
                    Assert.That(MaximumDistance(end, Nodes()), Is.LessThan(.002f),
                        "Every displayed physical sling node must cross the actual Idle loop seam without a visible jump.");
                    Assert.That(sling.TotalSimulationSteps, Is.EqualTo(physicalSteps),
                        "Crossing a recorded Idle loop must remain pure playback.");
                    camera.Save(pictures, "Idle with shotgun", false);
                    camera.Save(pictures, "Idle with shotgun", true);

                    void PresentAt(float seconds)
                    {
                        clip.SampleAnimation(registry.Animator.gameObject, seconds);
                        registry.GetComponent<CharacterJointDeformation>()?.ApplyPose();
                        registry.GetComponent<PlayerBootDeformation>()?.ApplyPose(FootGroundSample.None, FootGroundSample.None);
                        SecondaryMotionBakeRuntime.SetContext(registry, clip, seconds, "stored-shotgun");
                        Assert.That(sling.PreviewBakedPose(seconds / clip.length), Is.True,
                            "Stored-shotgun Idle seam must accept its physical recording: " +
                            SecondaryMotionBakeRuntime.DescribeContext(registry, SecondaryMotionBakePart.Sling));
                        Assert.That(sling.LastSimulationSteps, Is.Zero);
                        Assert.That(sling.SteadyProjectionIterations, Is.Zero);
                        Assert.That(sling.SteadyLengthRepairIterations, Is.Zero);
                        contacts.UpdatePose();
                        VerifyGeometry(contacts);
                    }
                }

                void VerifyWarmEntry()
                {
                    const float physicalStep = 1f / 120f;
                    registry.transform.SetPositionAndRotation(position, rotation);
                    // Real bounded root motion supplies inertia; the fixture
                    // never injects synthetic positions into the chain.
                    sling.SimulateBakeStep(physicalStep, .5f);
                    registry.transform.position = position + rotation * new Vector3(.24f, 0f, 0f);
                    sling.SimulateBakeStep(physicalStep, .5f);
                    registry.transform.position = position + rotation * new Vector3(.24f, 0f, .24f);
                    sling.SimulateBakeStep(physicalStep, .5f);
                    registry.transform.position = position;
                    sling.SimulateBakeStep(physicalStep, .5f);
                    SecondaryMotionBakeRuntime.SetContext(registry, clip, clip.length * .5f, "stored-shotgun");
                    Assert.That(SecondaryMotionBakeRuntime.TrySample(registry, SecondaryMotionBakePart.Sling,
                        sling.BakedPoseSignature, sling.BakedPoseValueCount, false, new WindSample(0f, 0f),
                        out SecondaryMotionBakeSample recording, "stored-shotgun"), Is.True);
                    float warmDifference = DistanceFromRecording();
                    Assert.That(warmDifference, Is.GreaterThan(.01f),
                        "Test root motion must leave a different live chain after all anchors return to the recorded pose.");
                    long pureCalls = sling.BakedPlaybackFrames;
                    Assert.That(sling.PreviewBakedTransition(.5f, 0f), Is.False);
                    Assert.That(sling.IsEnteringBakedPlayback, Is.True);
                    Assert.That(sling.BakedEntryElapsed, Is.Zero);
                    Assert.That(sling.BakedPlaybackFrames, Is.EqualTo(pureCalls));
                    Assert.That(DistanceFromRecording(), Is.GreaterThan(warmDifference * .25f),
                        "Accepting matching anchors must not immediately replace the displayed live chain.");

                    using var contacts = new PlayerScarfBodyContacts(registry, useMeshSupportPlanes: true,
                        includeEquipment: false, poseSourceSkin: true, pointQuerySlots: 41, jacketSurfacePadding: .02025f);
                    contacts.UpdatePose();
                    bool pure = false;
                    for (int frame = 0; frame < 16 && !pure; frame++)
                    {
                        pure = sling.PreviewBakedTransition(.5f, 1f / 30f);
                        VerifyGeometry(contacts);
                        if (!pure)
                        {
                            Assert.That(sling.BakedPlaybackFrames, Is.EqualTo(pureCalls),
                                "Contact/PBD reconciliation must not count as pure recorded playback.");
                            Assert.That(sling.BakedEntryElapsed, Is.EqualTo(Mathf.Min(.12f, (frame + 1) / 30f)).Within(.000001f),
                                "The entry clock uses presented time while physics remains bounded to two substeps.");
                            Assert.That(sling.LastSimulationSteps, Is.LessThanOrEqualTo(2));
                        }
                    }
                    Assert.That(pure, Is.True, System.FormattableString.Invariant($"A compatible contact-safe recording must finish its entry and bypass the live solver; decision={sling.LastBakedDecision}, elapsed={sling.BakedEntryElapsed:F6}s, fit={sling.BakedEntryFitError:F6}m, candidate contact={sling.BakedEntryCandidateContactError:F6}m, link={sling.BakedEntryCandidateLinkError:F6}m, length={sling.BakedEntryCandidateLengthError:F6}m."));
                    Assert.That(sling.IsEnteringBakedPlayback, Is.False);
                    Assert.That(sling.SteadyProjectionIterations, Is.Zero);
                    Assert.That(sling.LastSimulationSteps, Is.Zero);
                    long steps = sling.TotalSimulationSteps;
                    Assert.That(sling.PreviewBakedTransition(.5f, 1f / 30f), Is.True);
                    Assert.That(sling.TotalSimulationSteps, Is.EqualTo(steps));
                    Vector3[] displayed = Nodes();
                    pureCalls = sling.BakedPlaybackFrames;
                    Assert.That(sling.PreviewBakedTransition(.5f, 0f, useBaked: false), Is.False);
                    Assert.That(sling.BakedPlaybackFrames, Is.EqualTo(pureCalls));
                    Assert.That(MaximumDistance(Nodes(), displayed), Is.LessThan(.002f),
                        "Exiting playback must retain its complete displayed chain without an artificial launch velocity.");
                    VerifyGeometry(contacts);

                    float DistanceFromRecording()
                    {
                        float maximum = 0f;
                        for (int point = 0; point <= 40; point++)
                        {
                            int offset = point * 3;
                            Vector3 local = new Vector3(Value(offset), Value(offset + 1), Value(offset + 2));
                            maximum = Mathf.Max(maximum, Vector3.Distance(sling.WorldCenterlinePoint(point * 2), registry.transform.TransformPoint(local)));
                        }
                        return maximum;
                        float Value(int index) => Mathf.LerpUnclamped(recording.From[index], recording.To[index], recording.Alpha);
                    }

                }
            }
            finally
            {
                if (sling != null) sling.DisposeBakeSling();
                registry.transform.SetPositionAndRotation(position, rotation);
                SecondaryMotionBakeRuntime.Release(registry);
                PlayerEquipmentContacts equipment = registry.GetComponent<PlayerEquipmentContacts>();
                if (originalEquipment == null && equipment != null) Object.DestroyImmediate(equipment);
            }
        }
    }
}
