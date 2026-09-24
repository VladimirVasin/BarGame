using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused brook crossing capture and production-capsule traversal.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageFootbridge()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () => VillageFootbridgeShots(root));

            // Keep the rendered evidence even when a physical contract fails.
            VerifyVillageFootbridge(root);
            yield return WalkVillageFootbridge(root);
        }

        private static Shot[] VillageFootbridgeShots(AlpineVillageRoot root)
        {
            var crossing = AlpineVillageFootbridgePlan.Create(root.Plan);
            Assert.That(crossing, Is.Not.Null, "The forest approach needs its brook crossing.");
            Vector3 forward = Vector3.ProjectOnPlane(crossing.Forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 centre = crossing.Position;
            Vector3 firstEnd = centre - crossing.Forward * (crossing.Length * .5f);
            Vector3 secondEnd = centre + crossing.Forward * (crossing.Length * .5f);
            Debug.Log($"Village footbridge: {crossing.PathId}; local crossing " +
                $"{root.Plan.Expansion.ToLocal(crossing.Crossing):F3}; position {centre:F3}; " +
                $"width/span {AlpineVillageFootbridgePlan.Width:F3}/{crossing.Length:F3} m; " +
                $"bearing offsets {firstEnd.y - VillageFootbridgeGround(root.Plan, firstEnd).y:F3}/" +
                $"{secondEnd.y - VillageFootbridgeGround(root.Plan, secondEnd).y:F3} m.");
            Vector3 approach = VillageFootbridgeGround(root.Plan,
                centre - forward * (crossing.Length * .5f + 3f));
            bool moved = false;
            int settled = 0;
            bool Ready()
            {
                if (!moved)
                {
                    root.SetWarmthGrade(0f);
                    root.Player.Motor.Teleport(approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                    moved = true;
                }
                bool ready = ++settled > 12 && root.StormWave <= GustTroughWave;
                if (ready)
                    foreach (Renderer renderer in root.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                        renderer.enabled = false;
                return ready;
            }

            return new[]
            {
                Shot.At("footbridge-00-trail-approach", approach + Vector3.up * EyeHeight,
                    centre + Vector3.up * .3f, 64f, 0, Ready),
                Shot.At("footbridge-01-bank-and-water", centre + right * 5.8f - forward * 1.2f + Vector3.up * 1.6f,
                    centre + Vector3.up * .12f, 62f, 0, Ready),
                Shot.At("footbridge-02-crossing-overhead", centre + Vector3.up * 10f - forward * 1f,
                    centre, 64f, 0, Ready)
            };
        }

        private static void VerifyVillageFootbridge(AlpineVillageRoot root)
        {
            var crossing = AlpineVillageFootbridgePlan.Create(root.Plan);
            Assert.That(crossing, Is.Not.Null);
            Transform bridge = root.World.Root.transform.Find("Village Expansion/Brook Footbridge");
            Assert.That(bridge, Is.Not.Null);
            Assert.That(Vector3.Distance(bridge.position, crossing.Position), Is.LessThan(.005f));
            Assert.That(Quaternion.Angle(bridge.rotation, crossing.Rotation), Is.LessThan(.05f));
            Bounds actual = LocalRendererBounds(bridge);
            Vector3 measuredSize = Vector3.Scale(actual.size, bridge.lossyScale);
            Assert.That(measuredSize.x, Is.EqualTo(AlpineVillageFootbridgePlan.Width).Within(.08f), "Imported bridge width.");
            Assert.That(measuredSize.z, Is.EqualTo(crossing.Length).Within(.10f), "Imported bridge span.");
            Assert.That(actual.size.y, Is.InRange(.65f, 1.8f), "Imported bridge vertical scale.");
            MeshCollider[] solids = bridge.GetComponentsInChildren<MeshCollider>();
            Assert.That(solids, Is.Not.Empty, "The authored bridge must carry physical support.");
            foreach (MeshCollider solid in solids)
                Assert.That(solid.sharedMesh, Is.SameAs(solid.GetComponent<MeshFilter>().sharedMesh),
                    "Bridge collision must follow its visible authored part.");

            Physics.SyncTransforms();
            foreach (float end in new[] { -1f, 1f })
            foreach (float side in new[] { -.9f, 0f, .9f })
            {
                Vector3 toe = bridge.TransformPoint(new Vector3(side, 0f, end * 2.397f));
                Assert.That(TryVillageFootbridgeFloor(solids, toe, out float surface), Is.True);
                float pathHeight = VillageFootbridgeGround(root.Plan, toe).y + AlpineVillageWorldBuilder.LaneSkinLift;
                Assert.That(surface, Is.EqualTo(pathHeight).Within(.04f),
                    "The visible approach must meet its bank instead of hanging above it.");
            }
            float lastHeight = float.NaN;
            for (float along = -crossing.Length * .5f + .08f;
                 along < crossing.Length * .5f - .08f; along += .12f)
            {
                foreach (float lateral in new[] { -.50f, 0f, .50f })
                {
                    Vector3 point = crossing.Position + crossing.Rotation * new Vector3(lateral, 0f, along);
                    Assert.That(TryVillageFootbridgeFloor(solids, point, out float floor), Is.True,
                        $"The deck or ramp has no floor at {lateral:F2}/{along:F2}.");
                    Assert.That(root.World.WalkableArea.Contains(point, .35f), Is.True,
                        "The movement mask closes the visible brook crossing.");
                    if (lateral != 0f) continue;
                    if (!float.IsNaN(lastHeight))
                        Assert.That(Mathf.Abs(floor - lastHeight), Is.LessThan(.20f),
                            "A deck or ramp lip exceeds an ordinary walking step.");
                    lastHeight = floor;
                }
            }
            VerifyVillageFootbridgeWater(root, crossing.Crossing);
            Debug.Log($"Village footbridge imported width/span: {measuredSize.x:F3}/{measuredSize.z:F3} m.");
        }

        private static bool TryVillageFootbridgeFloor(MeshCollider[] solids, Vector3 point, out float height)
        {
            height = float.NegativeInfinity;
            foreach (MeshCollider solid in solids)
                if (solid.Raycast(new Ray(point + Vector3.up * 3f, Vector3.down),
                    out RaycastHit hit, 6f)) height = Mathf.Max(height, hit.point.y);
            return !float.IsNegativeInfinity(height);
        }

        private static void VerifyVillageFootbridgeWater(AlpineVillageRoot root, Vector3 crossing)
        {
            MeshFilter water = root.World.Root.transform.Find("Village Spring/Spring Brook Water")
                .GetComponent<MeshFilter>();
            Vector3[] vertices;
            int[] triangles;
#if UNITY_EDITOR
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(water.sharedMesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp))
            using (var indices = new Unity.Collections.NativeArray<int>(data[0].GetSubMesh(0).indexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(positions);
                data[0].GetIndices(indices, 0);
                vertices = positions.ToArray();
                triangles = indices.ToArray();
            }
#else
            vertices = water.sharedMesh.vertices;
            triangles = water.sharedMesh.triangles;
#endif
            for (int index = 0; index < vertices.Length; index++)
                vertices[index] = water.transform.TransformPoint(vertices[index]);
            // Reuse the capture fixture's generic XZ triangle-coverage probe.
            Assert.That(VillageAsphaltCovers(crossing, vertices, triangles), Is.True,
                "The bridge must span existing continuous water.");
            int probes = 0;
            var samples = root.Plan.Brook.Samples;
            for (int index = 0; index < samples.Count - 1; index++)
            {
                Vector3 middle = (samples[index].Position + samples[index + 1].Position) * .5f;
                if (Vector3.ProjectOnPlane(middle - crossing, Vector3.up).sqrMagnitude > 9f) continue;
                Assert.That(VillageAsphaltCovers(middle, vertices, triangles), Is.True,
                    "The brook must continue beneath and beside the bridge.");
                probes++;
            }
            Assert.That(probes, Is.GreaterThan(1));
        }

        private static IEnumerator WalkVillageFootbridge(AlpineVillageRoot root)
        {
            var crossing = AlpineVillageFootbridgePlan.Create(root.Plan);
            Transform bridge = root.World.Root.transform.Find("Village Expansion/Brook Footbridge");
            MeshCollider[] solids = bridge.GetComponentsInChildren<MeshCollider>();
            Vector3 forward = Vector3.ProjectOnPlane(crossing.Forward, Vector3.up).normalized;
            Vector3 near = VillageFootbridgeGround(root.Plan,
                crossing.Position - forward * (crossing.Length * .5f + .85f));
            Vector3 far = VillageFootbridgeGround(root.Plan,
                crossing.Position + forward * (crossing.Length * .5f + .85f));
            PlayerMotor motor = root.Player.Motor;
            float previousFrameSeconds = Time.captureDeltaTime;
            bool inputWasEnabled = motor.InputEnabled;
            try
            {
                const float stepSeconds = .04f;
                Time.captureDeltaTime = stepSeconds;
                motor.SetInputEnabled(false);
                motor.Teleport(near + Vector3.up * PlayerFactory.GroundedRootOffset);
                for (int frame = 0; frame < 15; frame++) yield return null;
                foreach (Vector3 destination in new[] { far, near })
                {
                    bool arrived = false;
                    int supported = 0;
                    for (int frame = 0; frame < 400 && !arrived; frame++)
                    {
                        arrived = motor.MoveTowardsApproachWaypoint(destination, .06f, stepSeconds);
                        // Keep normal Update gravity active during the real
                        // CharacterController walk, including both ramp joins.
                        yield return null;
                        Assert.That(motor.InteractionPoseMoveStalled, Is.False,
                            $"The hero stalled crossing the brook at {motor.transform.position:F3}.");
                        float along = Vector3.Dot(motor.transform.position - bridge.position, crossing.Forward);
                        if (Mathf.Abs(along) >= crossing.Length * .25f) continue;
                        Assert.That(TryVillageFootbridgeFloor(solids, motor.transform.position, out float floor), Is.True);
                        Assert.That(motor.transform.position.y,
                            Is.EqualTo(floor + PlayerFactory.GroundedRootOffset).Within(.18f),
                            "The hero must walk on the bridge instead of through the brook beneath it.");
                        supported++;
                    }
                    Assert.That(arrived, Is.True, "The hero did not reach the opposite bank.");
                    Assert.That(supported, Is.GreaterThan(0), "The traversal never crossed the bridge deck.");
                    motor.CancelInteractionPoseMove();
                    yield return null;
                }
            }
            finally
            {
                motor.CancelInteractionPoseMove();
                motor.SetInputEnabled(inputWasEnabled);
                Time.captureDeltaTime = previousFrameSeconds;
            }
        }

        private static Vector3 VillageFootbridgeGround(AlpineVillagePlan plan, Vector3 point)
        {
            var xz = new Vector2(point.x, point.z);
            point.y = Mathf.Max(AlpineVillageTerrainSampler.SampleHeight(plan, xz),
                AlpineVillageTerrainSampler.SampleMeshHeight(plan, xz));
            return point;
        }
    }
}
