using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Both source-scene stairwell doors: production DoorUse clips, imported lever triangles, measured placement and start/contact/return frames. No destination transition.")]
        [PrebuildSetup(typeof(StairwellAuthoredAssetsSetup))]
        public IEnumerator StairwellDoorHandleContacts()
        {
            const float maximumGripGap = .025f;
            StairwellInteriorRoot root = null;
            WarehouseRefrigeratorPresentationProbe probe = null;
            var report = new StairDoorContactReport();
            float previousDelta = Time.captureDeltaTime;
            bool previousFixedCamera = false, previousFollow = false, stateCaptured = false;
            Pose previousCamera = default;
            Vector3 previousHero = default;
            Quaternion previousHeroRotation = default;
            float previousFov = 0f, previousAspect = 0f;
            AnimatorCullingMode previousCulling = default;
            SkinnedMeshRenderer[] skins = null;
            bool[] previousRecalculation = null;
            try
            {
                GameSessionState.BeginNewGame("Test stairwell source door contacts");
                Time.captureDeltaTime = 1f / 60f;
                yield return SceneManager.LoadSceneAsync(SceneIds.StairwellInterior, LoadSceneMode.Single);
                yield return WaitForStairDoor(() => (root = Object.FindAnyObjectByType<StairwellInteriorRoot>()) != null &&
                    root.IsInitialized && !SceneTransitionService.IsTransitioning, "stairwell composition");
                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                Player3DAssetRegistry registry = presentation.Registry;
                Assert.That(registry.Anchors.RightGrip, Is.Not.Null);
                previousCulling = registry.Animator.cullingMode;
                registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                skins = root.Player.GameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                previousRecalculation = new bool[skins.Length];
                for (int i = 0; i < skins.Length; i++)
                {
                    previousRecalculation[i] = skins[i].forceMatrixRecalculationPerRender;
                    skins[i].forceMatrixRecalculationPerRender = true;
                }
                previousFixedCamera = root.FixedCamera.enabled;
                previousFollow = root.CameraFollow.enabled;
                previousCamera = new Pose(camera.transform.position, camera.transform.rotation);
                previousFov = camera.fieldOfView; previousAspect = camera.aspect;
                previousHero = root.Player.GameObject.transform.position;
                previousHeroRotation = root.Player.GameObject.transform.rotation;
                stateCaptured = true;
                root.FixedCamera.enabled = false; root.CameraFollow.enabled = false;
                camera.aspect = (float)Width / Height;
                // Reuse the existing final-presentation scheduler after the real
                // interaction/controller LateUpdates; its name is historical.
                probe = root.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
                foreach (string doorName in new[] { "Street", "Apartment" })
                {
                    Component interactable = doorName == "Street" ? (Component)root.StreetExit : root.ApartmentEntrance;
                    PlayerDoorActionTarget target = interactable.GetComponent<PlayerDoorActionTarget>();
                    PlayerDoorActionPlan plan = target.Plan;
                    Transform door = root.World.Root.Find(doorName + " Door");
                    Transform hardware = door.Find(doorName + " Door Hardware");
                    var lever = new StairDoorLeverSurface(hardware);
                    root.Player.Motor.Teleport(plan.EntryRootPosition);
                    root.Player.GameObject.transform.rotation = plan.EntryRotation;
                    Physics.SyncTransforms();
                    camera.transform.SetPositionAndRotation(
                        plan.EntryRootPosition + plan.EntryRotation * new Vector3(.65f, 1.45f, -.45f),
                        Quaternion.LookRotation(plan.EntryRootPosition + Vector3.up * 1.1f + plan.EntryFacingDirection * .4f -
                            (plan.EntryRootPosition + plan.EntryRotation * new Vector3(.65f, 1.45f, -.45f))));
                    camera.fieldOfView = 58f;
                    yield return null;
                    yield return AtWarehouseFridgePresentation(probe, () =>
                        CaptureCurrentCamera(camera, SceneIds.StairwellInterior, "Door contact - " + doorName + " - 01 start"));
                    int completed = 0;
                    Assert.That(target.TryBegin(root.Player.Interactor, () => completed++), Is.True);
                    yield return WaitForStairDoor(() => root.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Looping,
                        doorName + " DoorUseLoop");
                    yield return AtWarehouseFridgePresentation(probe, () =>
                    {
                        Assert.That(presentation.ActiveClipName, Is.EqualTo("DoorUseLoop"));
                        Assert.That(Vector3.Distance(root.Player.GameObject.transform.position, plan.EntryRootPosition), Is.LessThan(.01f));
                        StairDoorContactRow row = lever.Measure(doorName, door.Find(doorName + " Door Leaf"),
                            registry.Anchors.RightGrip.position, root.Player.GameObject.transform);
                        report.doors.Add(row);
                        WriteStairDoorContactReport(report);
                        TestContext.Progress.WriteLine("STAIR_DOOR_CONTACT_MEASURED " + JsonUtility.ToJson(row));
                        CaptureCurrentCamera(camera, SceneIds.StairwellInterior, "Door contact - " + doorName + " - 02 loop");
                    });
                    yield return WaitForStairDoor(() => completed == 1 && !root.AnimatedInteraction.IsActive,
                        doorName + " terminal neutral return");
                    yield return AtWarehouseFridgePresentation(probe, () =>
                    {
                        Assert.That(presentation.IsClipActive, Is.False);
                        Assert.That(root.Player.Motor.InputEnabled && root.Player.Interactor.InputEnabled, Is.True);
                        CaptureCurrentCamera(camera, SceneIds.StairwellInterior, "Door contact - " + doorName + " - 03 return");
                    });
                    Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.StairwellInterior));
                }
                Assert.That(report.doors.Count, Is.EqualTo(2));
                // Write BOTH measurements before this regression gate. A failed
                // contact is evidence for the fix, never a passed art acceptance.
                foreach (StairDoorContactRow row in report.doors)
                {
                    Assert.That(row.gripToCurrentLeverSurface, Is.LessThanOrEqualTo(maximumGripGap),
                        row.door + ": source DoorUse misses its visible lever. Read the measured hardware proposal and normal gap.");
                    Assert.That(Mathf.Abs(row.gripNormalToCurrentFront), Is.LessThanOrEqualTo(.005f),
                        row.door + ": the right grip must meet the outward lever face, not pass through to its back.");
                    Assert.That(row.minimumBackplatePenetration, Is.GreaterThanOrEqualTo(.001f));
                    Assert.That(row.maximumBackplatePenetration, Is.LessThanOrEqualTo(.003f),
                        row.door + ": the full escutcheon must mount on the actual full-depth leaf stile.");
                }
            }
            finally
            {
                if (probe != null) { probe.CancelPending(); Object.Destroy(probe); }
                if (root != null && stateCaptured)
                {
                    root.AnimatedInteraction.CancelActiveInteraction();
                    var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                    presentation.Registry.Animator.cullingMode = previousCulling;
                    if (skins != null)
                        for (int i = 0; i < skins.Length; i++)
                            if (skins[i] != null) skins[i].forceMatrixRecalculationPerRender = previousRecalculation[i];
                    root.Player.Motor.Teleport(previousHero);
                    root.Player.GameObject.transform.rotation = previousHeroRotation;
                    root.FixedCamera.enabled = previousFixedCamera;
                    root.CameraFollow.enabled = previousFollow;
                    if (Camera.main != null && previousFov > 0f)
                    {
                        Camera.main.transform.SetPositionAndRotation(previousCamera.position, previousCamera.rotation);
                        Camera.main.fieldOfView = previousFov; Camera.main.aspect = previousAspect;
                    }
                }
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator WaitForStairDoor(Func<bool> ready, string operation)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, operation);
        }

        private static void WriteStairDoorContactReport(StairDoorContactReport report)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.StairwellInterior);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Door contact measurements.json"), JsonUtility.ToJson(report, true));
        }

        [Serializable]
        private sealed class StairDoorContactReport
        {
            public string clip = "DoorUseLoop on the production world hero; existing source-scene dock";
            public List<StairDoorContactRow> doors = new List<StairDoorContactRow>();
        }

        [Serializable]
        private sealed class StairDoorContactRow
        {
            public string door;
            public Vector3 gripWorld, gripInHero, nearestCurrentLeverWorld, currentHardwareLocal;
            public Vector3 suggestedHardwareLocal, suggestedWorldDelta;
            public float gripToCurrentLeverSurface, gripToSuggestedLeverSurface, suggestedMountNormalDelta;
            public float gripNormalToCurrentFront, gripNormalToSuggestedFront;
            public float minimumBackplatePenetration, maximumBackplatePenetration;
            public bool suggestedMountHitsActualLeaf;
        }

        /// <summary>Find the actual disconnected rounded lever solid, then
        /// measure its triangles. Bounds identify the part; they are not the
        /// contact-distance proof. No collider or scene object is added.</summary>
        private sealed class StairDoorLeverSurface
        {
            private readonly Transform hardware;
            private readonly Vector3[] vertices;
            private readonly int[] triangles;
            private readonly List<int> leverTriangles = new List<int>();
            private readonly Vector3 frontCentre, plateBack;
            private readonly Bounds plateBounds;

            public StairDoorLeverSurface(Transform hardware)
            {
                this.hardware = hardware;
                Mesh mesh = hardware.GetComponent<MeshFilter>().sharedMesh;
                vertices = mesh.vertices; triangles = mesh.triangles;
                Assert.That(vertices.Length, Is.LessThanOrEqualTo(8192));
                var parents = new int[vertices.Length];
                var welded = new Dictionary<Vector3Int, int>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    parents[i] = i;
                    Vector3Int key = Vector3Int.RoundToInt(vertices[i] * 100000f);
                    if (welded.TryGetValue(key, out int first)) parents[i] = first;
                    else welded.Add(key, i);
                }
                int Find(int value) { while (parents[value] != value) value = parents[value]; return value; }
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int first = Find(triangles[i]);
                    parents[Find(triangles[i + 1])] = first; parents[Find(triangles[i + 2])] = first;
                }
                var components = new Dictionary<int, Bounds>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    int component = Find(i);
                    if (!components.TryGetValue(component, out Bounds bounds)) bounds = new Bounds(vertices[i], Vector3.zero);
                    bounds.Encapsulate(vertices[i]); components[component] = bounds;
                }
                int selected = -1;
                Bounds lever = default, plate = default;
                foreach (var pair in components)
                {
                    Vector3 size = Vector3.Scale(pair.Value.size, hardware.localScale);
                    if (Mathf.Abs(size.x - .240f) < .003f && size.y < .045f && size.z < .032f)
                    { Assert.That(selected, Is.EqualTo(-1)); selected = pair.Key; lever = pair.Value; }
                    if (Mathf.Abs(size.y - .240f) < .003f && size.x < .070f) plate = pair.Value;
                }
                Assert.That(selected, Is.GreaterThanOrEqualTo(0), "The imported hardware must contain its actual .240 m rounded lever solid.");
                Assert.That(plate.size.y, Is.GreaterThan(0f), "The backplate must remain a distinct mounting solid.");
                for (int i = 0; i < triangles.Length; i += 3)
                    if (Find(triangles[i]) == selected) leverTriangles.Add(i);
                plateBounds = plate;
                frontCentre = hardware.TransformPoint(lever.center + Vector3.forward * lever.extents.z);
                plateBack = hardware.TransformPoint(plate.center - Vector3.forward * plate.extents.z);
            }

            public StairDoorContactRow Measure(string name, Transform leaf, Vector3 grip, Transform hero)
            {
                Vector3 nearest = Nearest(grip, Vector3.zero);
                Vector3 normal = hardware.forward;
                Vector3 delta = grip - frontCentre;
                delta -= normal * Vector3.Dot(delta, normal);
                Vector3 proposedPlate = plateBack + delta;
                // Preserve the real leaf mounting contact, including any inset
                // face at the newly measured in-plane location. No runtime IK.
                bool hit = TryLeafSurface(leaf, proposedPlate + normal * .5f, -normal, out Vector3 leafPoint);
                float mount = hit ? Vector3.Dot(leafPoint - proposedPlate, normal) - .002f : 0f;
                delta += normal * mount;
                float minMount = float.PositiveInfinity, maxMount = float.NegativeInfinity;
                for (int y = 0; y < 5; y++)
                    for (int x = 0; x < 5; x++)
                    {
                        // Stay inside the backplate's own .004 m edge chamfer.
                        float insetX = .005f / hardware.localScale.x, insetY = .005f / hardware.localScale.y;
                        Vector3 local = new Vector3(Mathf.Lerp(plateBounds.min.x + insetX, plateBounds.max.x - insetX, x / 4f),
                            Mathf.Lerp(plateBounds.min.y + insetY, plateBounds.max.y - insetY, y / 4f), plateBounds.min.z);
                        Vector3 back = hardware.TransformPoint(local);
                        if (!TryLeafSurface(leaf, back + normal * .5f, -normal, out Vector3 support))
                        { minMount = float.NegativeInfinity; maxMount = float.PositiveInfinity; continue; }
                        float penetration = Vector3.Dot(support - back, normal);
                        minMount = Mathf.Min(minMount, penetration); maxMount = Mathf.Max(maxMount, penetration);
                    }
                return new StairDoorContactRow
                {
                    door = name, gripWorld = grip, gripInHero = hero.InverseTransformPoint(grip),
                    nearestCurrentLeverWorld = nearest, currentHardwareLocal = hardware.localPosition,
                    gripToCurrentLeverSurface = Vector3.Distance(grip, nearest),
                    suggestedHardwareLocal = hardware.parent.InverseTransformPoint(hardware.position + delta),
                    suggestedWorldDelta = delta, gripToSuggestedLeverSurface = Vector3.Distance(grip, Nearest(grip, delta)),
                    suggestedMountHitsActualLeaf = hit, suggestedMountNormalDelta = mount,
                    gripNormalToCurrentFront = Vector3.Dot(grip - frontCentre, normal),
                    gripNormalToSuggestedFront = Vector3.Dot(grip - frontCentre - delta, normal),
                    minimumBackplatePenetration = minMount, maximumBackplatePenetration = maxMount
                };
            }

            private Vector3 Nearest(Vector3 point, Vector3 translation)
            {
                Vector3 closest = default;
                float best = float.PositiveInfinity;
                foreach (int triangle in leverTriangles)
                {
                    Vector3 a = hardware.TransformPoint(vertices[triangles[triangle]]) + translation;
                    Vector3 b = hardware.TransformPoint(vertices[triangles[triangle + 1]]) + translation;
                    Vector3 c = hardware.TransformPoint(vertices[triangles[triangle + 2]]) + translation;
                    Vector3 candidate = StairDoorTrianglePoint(point, a, b, c);
                    float distance = (point - candidate).sqrMagnitude;
                    if (distance < best) { best = distance; closest = candidate; }
                }
                return closest;
            }

            private static bool TryLeafSurface(Transform leaf, Vector3 origin, Vector3 direction, out Vector3 point)
            {
                Mesh mesh = leaf.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] source = mesh.vertices;
                int[] indices = mesh.triangles;
                float best = float.PositiveInfinity;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = leaf.TransformPoint(source[indices[i]]), b = leaf.TransformPoint(source[indices[i + 1]]),
                        c = leaf.TransformPoint(source[indices[i + 2]]);
                    Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(direction, ac);
                    float determinant = Vector3.Dot(ab, p);
                    if (Mathf.Abs(determinant) < 1e-8f) continue;
                    Vector3 offset = origin - a;
                    float u = Vector3.Dot(offset, p) / determinant;
                    Vector3 q = Vector3.Cross(offset, ab);
                    float v = Vector3.Dot(direction, q) / determinant, distance = Vector3.Dot(ac, q) / determinant;
                    if (u >= 0f && v >= 0f && u + v <= 1f && distance >= 0f && distance < 1f) best = Mathf.Min(best, distance);
                }
                point = float.IsPositiveInfinity(best) ? default : origin + direction * best;
                return !float.IsPositiveInfinity(best);
            }
        }

        private static Vector3 StairDoorTrianglePoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
            float inverse = 1f / (va + vb + vc);
            return a + ab * (vb * inverse) + ac * (vc * inverse);
        }
    }
}
