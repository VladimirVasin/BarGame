using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Five ordinary workers sampled from the port's absolute, seekable timeline.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class CityPortCrew : MonoBehaviour
    {
        private CityPortController port;
        private Transform captainDock, helmLeft, helmRight, deckhandDock;
        private Transform bowCleat, sternCleat, trolleyHandle;
        private readonly Transform[] operatorDocks = new Transform[2];
        private readonly Transform[] controlsLeft = new Transform[2];
        private readonly Transform[] controlsRight = new Transform[2];
        private readonly Transform[] shoreCleats = new Transform[2];
        private readonly VillageResidentPresentation[] workers = new VillageResidentPresentation[5];
        private readonly Transform[] spines = new Transform[5];
        private readonly Color[] clothingTints =
        {
            new Color(.72f, .82f, 1.18f), new Color(1.55f, .89f, .74f),
            new Color(.97f, 1.06f, 1.13f), new Color(1.20f, 1.05f, .78f),
            new Color(1.72f, 1.10f, .87f)
        };
        private readonly Color[] knitTints =
        {
            new Color(.76f, .81f, .94f), new Color(.80f, .77f, .72f),
            new Color(1.20f, 1.16f, 1.07f), new Color(.79f, .85f, .78f),
            new Color(.76f, .73f, .69f)
        };
        private readonly Color[] gloveTints =
        {
            new Color(.64f, .67f, .70f), new Color(1.17f, 1.10f, 1.01f),
            new Color(.83f, .82f, .78f), new Color(.94f, .89f, .78f),
            new Color(1.28f, 1.23f, 1.12f)
        };
        private Renderer[][] clothing;
        private Color[][] originalColors;
        private Vector4[][] fabricUvTransforms;
        private Texture fabricTexture;
        private MaterialPropertyBlock block;
        private bool initialized;
        private Vector3[] deckRoute;
        private Vector3[] shoreRoute;
        public VillageResidentPresentation Captain => workers[0];
        public VillageResidentPresentation Deckhand => workers[1];
        public VillageResidentPresentation FirstCraneOperator => workers[2];
        public VillageResidentPresentation SecondCraneOperator => workers[3];
        public VillageResidentPresentation ShoreWorker => workers[4];
        public int WorkerCount => workers.Length;
        public bool CaptainHandsMatch { get; private set; }
        public bool CraneHandsMatch { get; private set; }
        public bool TrolleyHandsMatch { get; private set; }
        public CityPortCycleSnapshot LastSnapshot { get; private set; }

        public static CityPortCrew Build(Transform parent, CityPortController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            var library = VillageResidentLibrary.Load();
            if (library == null || library.GetPrefab(VillageResidentRole.StationWorker) == null)
                throw new InvalidOperationException("The port requires the authored ordinary worker rig.");
            var host = new GameObject("Port Crew");
            host.transform.SetParent(parent, false);
            var crew = host.AddComponent<CityPortCrew>();
            crew.block = new MaterialPropertyBlock();
            crew.port = controller;
            crew.captainDock = Require(controller.Vessel, "ANCHOR_Captain");
            crew.helmLeft = Require(controller.Vessel, "ANCHOR_HelmLeft");
            crew.helmRight = Require(controller.Vessel, "ANCHOR_HelmRight");
            crew.deckhandDock = Require(controller.Vessel, "ANCHOR_Deckhand");
            crew.bowCleat = Require(controller.Vessel, "ANCHOR_MoorBow");
            crew.sternCleat = Require(controller.Vessel, "ANCHOR_MoorStern");
            crew.trolleyHandle = Require(controller.Trolley, "ANCHOR_Handle");
            for (int i = 0; i < 2; i++)
            {
                crew.operatorDocks[i] = Require(controller.CraneBases[i], "ANCHOR_Operator");
                crew.controlsLeft[i] = Require(controller.CraneBases[i], "ANCHOR_ControlLeft");
                crew.controlsRight[i] = Require(controller.CraneBases[i], "ANCHOR_ControlRight");
                crew.shoreCleats[i] = Require(controller.transform,
                    i == 0 ? "ANCHOR_BollardWest" : "ANCHOR_BollardEast");
            }

            string[] names = { "Captain", "Deckhand", "West Crane Operator", "East Crane Operator", "Quay Worker" };
            float[] scales = { 1f, .97f, 1.015f, .985f, 1f };
            crew.clothing = new Renderer[5][];
            crew.originalColors = new Color[5][];
            crew.fabricUvTransforms = new Vector4[5][];
            Material fabric = CityPortAssetProvider.GetSurfaceMaterial("Fabric");
            crew.fabricTexture = fabric.GetTexture("_BaseMap");
            for (int i = 0; i < crew.workers.Length; i++)
            {
                // The library prefab contains only the passive rig/presentation.
                // No village life controller, greeting or interaction is installed.
                var actor = library.Create(VillageResidentRole.StationWorker, host.transform);
                actor.name = names[i];
                actor.transform.localScale *= scales[i];
                crew.workers[i] = actor;
                crew.spines[i] = Require(actor.ModelRoot, "spine");
                crew.clothing[i] = Array.FindAll(actor.GetComponentsInChildren<Renderer>(true),
                    renderer => renderer.name.StartsWith("CLO_", StringComparison.Ordinal) || IsGlove(renderer.name));
                crew.originalColors[i] = new Color[crew.clothing[i].Length];
                crew.fabricUvTransforms[i] = new Vector4[crew.clothing[i].Length];
                for (int j = 0; j < crew.clothing[i].Length; j++)
                {
                    Renderer renderer = crew.clothing[i][j];
                    renderer.GetPropertyBlock(crew.block);
                    crew.originalColors[i][j] = crew.block.GetColor("_BaseColor");
                    crew.block.Clear();
                    if (!IsFabric(renderer.name)) continue;
                    crew.fabricUvTransforms[i][j] = FabricUvTransform(renderer);
                    renderer.sharedMaterial = fabric;
                }
            }
            crew.deckRoute = new Vector3[5];
            crew.shoreRoute = new Vector3[6];
            crew.initialized = true;
            crew.ApplyAt(controller.ElapsedSeconds);
            return crew;
        }

        private void LateUpdate()
        {
            if (port != null) ApplyAt(port.ElapsedSeconds);
        }

        public void ApplyAt(double seconds)
        {
            if (port == null || !initialized) return;
            LastSnapshot = CityPortCycle.Sample(seconds);
            float t = (float)LastSnapshot.SecondsInStage;
            SetVisible(Captain, LastSnapshot.VesselPresent);
            SetVisible(Deckhand, LastSnapshot.VesselPresent);
            if (LastSnapshot.VesselPresent)
            {
                Captain.transform.SetPositionAndRotation(captainDock.position, port.Vessel.rotation);
                Captain.Apply(VillageResidentAction.Idle, (float)(seconds % 60d));
                ApplyPlantedTorso(0, 3f, .8f, seconds);
                ApplyTaskLook(Captain, captainDock.position + port.Vessel.forward * 12f + port.Vessel.up * 1.4f);
                CaptainHandsMatch = Captain.ApplyHandContacts(helmRight.position, helmLeft.position);
                ApplyDeckhand(t);
            }

            CraneHandsMatch = true;
            for (int i = 0; i < 2; i++)
            {
                var actor = workers[i + 2];
                actor.transform.SetPositionAndRotation(operatorDocks[i].position, port.transform.rotation);
                float cargoTime = (float)LastSnapshot.SecondsInCargo;
                bool active = LastSnapshot.ActiveCraneIndex == i;
                float work = active ? cargoTime < CityPortCycle.LandedAtSeconds ?
                    EaseWindow(cargoTime, (float)CityPortCycle.LandedAtSeconds, 1f) :
                    cargoTime >= CityPortCycle.StoredAtSeconds ? EaseWindow(cargoTime - (float)CityPortCycle.StoredAtSeconds,
                        (float)(CityPortCycle.CargoDurationSeconds - CityPortCycle.StoredAtSeconds), .7f) : 0f : 0f;
                // A slight planted lean keeps the waist-height controls in
                // reach even while the operator waits with both hands resting.
                actor.Apply(VillageResidentAction.StationWork, Mathf.Lerp(1.02f, 1.4f, work));
                ApplyPlantedTorso(i + 2, .6f, .9f, seconds + i * 2.1d);
                ApplyTaskLook(actor, port.Hooks[i].position, work);
                CraneHandsMatch &= actor.ApplyHandContacts(controlsRight[i].position, controlsLeft[i].position);
            }
            ApplyShoreWorker(t);
            for (int i = 0; i < workers.Length; i++) Tint(i);
        }

        private void ApplyDeckhand(float t)
        {
            Vector3 bow = ShipCleatDock(bowCleat, true);
            Vector3 stern = ShipCleatDock(sternCleat, false);
            Vector3 hatch = HatchDock(LastSnapshot.CargoIndex % 2);
            switch (LastSnapshot.Stage)
            {
                case CityPortCycleStage.Moor:
                    if (t < 5f) Moor(Deckhand, bow, bowCleat.position, t, 5f, port.Vessel.up);
                    else if (t < 21f) WalkDeck(bow, stern, (t - 5f) / 16f, 16f);
                    else Moor(Deckhand, stern, sternCleat.position, t - 21f, 5f, port.Vessel.up);
                    break;
                case CityPortCycleStage.Prepare:
                    WalkDeck(stern, HatchDock(0), t / (float)CityPortCycle.PrepareDurationSeconds,
                        (float)CityPortCycle.PrepareDurationSeconds);
                    break;
                case CityPortCycleStage.Unload:
                    float c = (float)LastSnapshot.SecondsInCargo;
                    if (c >= 12f && c < 24f)
                        WalkDeck(hatch, HatchDock(1 - LastSnapshot.CargoIndex % 2), (c - 12f) / 12f, 12f);
                    else
                    {
                        Vector3 dock = c >= 24f ? HatchDock(1 - LastSnapshot.CargoIndex % 2) : hatch;
                        Vector3 look = port.Hooks[LastSnapshot.ActiveCraneIndex].position;
                        Stand(Deckhand, dock, port.Vessel.right, (float)LastSnapshot.SecondsInStage, look, port.Vessel.up);
                    }
                    break;
                case CityPortCycleStage.Secure:
                    WalkDeck(HatchDock(0), stern, t / (float)CityPortCycle.SecureDurationSeconds,
                        (float)CityPortCycle.SecureDurationSeconds);
                    break;
                case CityPortCycleStage.Unmoor:
                    if (t < 4f) Moor(Deckhand, stern, sternCleat.position, t, 4f, port.Vessel.up);
                    else if (t < 16.5f) WalkDeck(stern, bow, (t - 4f) / 12.5f, 12.5f);
                    else Moor(Deckhand, bow, bowCleat.position, t - 16.5f, 3.5f, port.Vessel.up);
                    break;
                default:
                    Stand(Deckhand, bow, bowCleat.position - bow, t, null, port.Vessel.up);
                    break;
            }
        }

        private void ApplyShoreWorker(float t)
        {
            Vector3 west = ShoreCleatDock(0), east = ShoreCleatDock(1);
            TrolleyHandsMatch = true;
            switch (LastSnapshot.Stage)
            {
                case CityPortCycleStage.Moor:
                    if (t < 5f) Moor(ShoreWorker, west, shoreCleats[0].position, t, 5f, Vector3.up);
                    else if (t < 21f) WalkShore(west, east, (t - 5f) / 16f, 16f, true);
                    else Moor(ShoreWorker, east, shoreCleats[1].position, t - 21f, 5f, Vector3.up);
                    break;
                case CityPortCycleStage.Prepare:
                    WalkShore(east, TrolleyDock(), t / (float)CityPortCycle.PrepareDurationSeconds,
                        (float)CityPortCycle.PrepareDurationSeconds, false);
                    break;
                case CityPortCycleStage.Unload:
                    Vector3 dock = TrolleyDock();
                    ShoreWorker.transform.SetPositionAndRotation(dock, port.Trolley.rotation);
                    bool moving = LastSnapshot.CargoStage == CityPortCargoStage.Trolley ||
                        LastSnapshot.CargoStage == CityPortCargoStage.Return;
                    float speed = moving ? port.TrolleyOperatorSpeed : 0f;
                    float gait = (float)LastSnapshot.SecondsInStage;
                    if (port.IsTrolleyReversing)
                    {
                        float length = ShoreWorker.ClipLength(VillageResidentAction.Walk);
                        gait = length - Mathf.Repeat(gait, length);
                    }
                    ShoreWorker.ApplyLocomotion(speed, false, gait);
                    // The planted spine carries the effort, not elongated arms.
                    // The original gait still owns every foot and the cart's
                    // measured operator dock still owns the complete root path.
                    float effort = Mathf.Clamp01(speed / 1.8f);
                    ApplyPlantedTorso(4, Mathf.Lerp(4f, port.IsTrolleyReversing ? 2f : 8f, effort),
                        Mathf.Lerp(.7f, .3f, effort), port.ElapsedSeconds);
                    Vector3 attention = moving ? trolleyHandle.position + port.Trolley.forward * 2f :
                        port.Hooks[Mathf.Max(0, LastSnapshot.ActiveCraneIndex)].position;
                    ApplyTaskLook(ShoreWorker, attention, .75f);
                    Vector3 across = port.Trolley.right * .34f;
                    float handWeight = EaseWindow((float)LastSnapshot.SecondsInStage,
                        (float)CityPortCycle.UnloadDurationSeconds, .8f);
                    TrolleyHandsMatch = ShoreWorker.ApplyHandContacts(trolleyHandle.position + across,
                        trolleyHandle.position - across, handWeight);
                    break;
                case CityPortCycleStage.Secure:
                    WalkShore(TrolleyDock(), east, t / (float)CityPortCycle.SecureDurationSeconds,
                        (float)CityPortCycle.SecureDurationSeconds, false);
                    break;
                case CityPortCycleStage.Unmoor:
                    if (t < 4f) Moor(ShoreWorker, east, shoreCleats[1].position, t, 4f, Vector3.up);
                    else if (t < 16.5f) WalkShore(east, west, (t - 4f) / 12.5f, 12.5f, true);
                    else Moor(ShoreWorker, west, shoreCleats[0].position, t - 16.5f, 3.5f, Vector3.up);
                    break;
                default:
                    Stand(ShoreWorker, west, Vector3.forward, t);
                    break;
            }
        }

        private Vector3 ShipCleatDock(Transform cleat, bool bow)
        {
            Vector3 local = port.Vessel.InverseTransformPoint(cleat.position);
            local.z -= .48f;
            // The forward work deck rises from 1.7 m at z=6 to 2 m at z=8.
            local.y = bow ? Mathf.Lerp(1.7f, 2f, (local.z - 6f) / 2f) : 1.6f;
            return port.Vessel.TransformPoint(local);
        }

        private Vector3 HatchDock(int crane)
        {
            Vector3 offset = port.Vessel.forward * (crane == 0 ? 8f : 0f);
            return deckhandDock.position + offset;
        }

        private Vector3 ShoreCleatDock(int index) =>
            new Vector3(shoreCleats[index].position.x, port.Plan.QuayTopY, shoreCleats[index].position.z - .6f);

        private Vector3 TrolleyDock()
        {
            Vector3 p = trolleyHandle.position - port.Trolley.forward * .48f;
            p.y = port.Plan.QuayTopY;
            return p;
        }

        private void WalkDeck(Vector3 from, Vector3 to, float progress, float duration)
        {
            // The port-side aisle runs outside both apertures and around the cabin.
            Vector3 side = deckhandDock.position;
            Vector3 up = port.Vessel.up, along = port.Vessel.forward;
            float a = Vector3.Dot(from - side, along), b = Vector3.Dot(to - side, along);
            deckRoute[0] = from;
            deckRoute[1] = side + along * Mathf.Clamp(a, -3.8f, 9.5f);
            deckRoute[2] = side + along * Mathf.Lerp(a, b, .5f);
            deckRoute[3] = side + along * Mathf.Clamp(b, -3.8f, 9.5f);
            deckRoute[4] = to;
            Walk(Deckhand, deckRoute, progress, duration, up, DeckFacing(from), DeckFacing(to));
            Vector3 local = port.Vessel.InverseTransformPoint(Deckhand.transform.position);
            // Follow the foredeck instead of carrying the bow's raised
            // endpoint height down the flat side aisle. The short transition
            // crosses its authored 10 cm lip as a step, not a long ramp in air.
            local.y = local.z <= 5.94f ? 1.6f : local.z < 6.06f ?
                Mathf.Lerp(1.6f, 1.709f, (local.z - 5.94f) / .12f) :
                Mathf.Lerp(1.7f, 2f, (local.z - 6f) / 2f);
            Deckhand.transform.position = port.Vessel.TransformPoint(local);
        }

        private void WalkShore(Vector3 from, Vector3 to, float progress, float duration, bool alongEdge)
        {
            shoreRoute[0] = from;
            if (alongEdge)
            {
                // The 2.7 m crane plinths end at z=-.65. Pass in front
                // of them, turning inboard before each mooring bollard.
                float direction = Mathf.Sign(to.x - from.x);
                float first = from.x + direction * .75f, last = to.x - direction * .75f;
                float lane = port.Plan.Origin.z - .30f;
                shoreRoute[1] = new Vector3(first, port.Plan.QuayTopY, from.z);
                shoreRoute[2] = new Vector3(first, port.Plan.QuayTopY, lane);
                shoreRoute[3] = new Vector3(last, port.Plan.QuayTopY, lane);
                shoreRoute[4] = new Vector3(last, port.Plan.QuayTopY, to.z);
            }
            else
            {
                float lane = port.Plan.Origin.z - 7.6f;
                shoreRoute[1] = new Vector3(from.x, port.Plan.QuayTopY, lane);
                shoreRoute[2] = new Vector3(to.x, port.Plan.QuayTopY, lane);
                shoreRoute[3] = shoreRoute[4] = to;
            }
            shoreRoute[5] = to;
            Walk(ShoreWorker, shoreRoute, progress, duration, Vector3.up, ShoreFacing(from), ShoreFacing(to));
        }

        private Vector3 DeckFacing(Vector3 point) =>
            Vector3.Distance(point, HatchDock(0)) < .05f || Vector3.Distance(point, HatchDock(1)) < .05f
                ? port.Vessel.right : port.Vessel.forward;

        private Vector3 ShoreFacing(Vector3 point) =>
            Vector3.Distance(point, TrolleyDock()) < .05f ? port.Trolley.forward : Vector3.forward;

        private static void Walk(VillageResidentPresentation actor, Vector3[] route, float progress,
            float duration, Vector3 up, Vector3 fromFacing, Vector3 toFacing)
        {
            const float turnSeconds = .65f;
            float seconds = Mathf.Clamp01(progress) * duration;
            float walkDuration = duration - 2f * turnSeconds;
            float t = Mathf.Clamp01((seconds - turnSeconds) / walkDuration), length = 0f;
            for (int i = 1; i < route.Length; i++) length += Vector3.Distance(route[i - 1], route[i]);
            float distance = Smooth(t) * length;
            Vector3 point = Along(route, distance);
            Vector3 direction = Along(route, Mathf.Min(length, distance + .3f)) -
                Along(route, Mathf.Max(0f, distance - .3f));
            if (direction.sqrMagnitude < .0001f) direction = route[route.Length - 1] - route[0];
            direction = Vector3.ProjectOnPlane(direction, up);
            if (direction.sqrMagnitude < .0001f) direction = actor.transform.forward;
            Quaternion rotation = Quaternion.LookRotation(direction, up);
            if (seconds < turnSeconds)
                rotation = Quaternion.Slerp(Quaternion.LookRotation(fromFacing, up), rotation, Smooth(seconds / turnSeconds));
            else if (seconds > duration - turnSeconds)
                rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(toFacing, up),
                    Smooth((seconds - duration + turnSeconds) / turnSeconds));
            actor.transform.SetPositionAndRotation(point, rotation);
            float speed = 6f * t * (1f - t) * length / walkDuration;
            actor.ApplyLocomotion(speed, false, distance / .95f);
        }

        private static Vector3 Along(Vector3[] route, float distance)
        {
            for (int i = 1; i < route.Length; i++)
            {
                float segment = Vector3.Distance(route[i - 1], route[i]);
                if (distance <= segment) return Vector3.Lerp(route[i - 1], route[i], segment < .0001f ? 0f : distance / segment);
                distance -= segment;
            }
            return route[route.Length - 1];
        }

        private static void Moor(VillageResidentPresentation actor, Vector3 dock, Vector3 cleat,
            float seconds, float duration, Vector3 up)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cleat - dock, up).normalized;
            actor.transform.SetPositionAndRotation(dock, Quaternion.LookRotation(forward, up));
            float sample = Mathf.Clamp01(seconds / duration) * 6f;
            actor.Apply(VillageResidentAction.StationStrap, sample);
            float weight = EaseWindow(sample, 6f, 1.5f);
            ApplyTaskLook(actor, cleat, weight);
            actor.ApplyHandContacts(cleat + actor.transform.right * .06f,
                cleat - actor.transform.right * .06f, weight);
        }

        private static void Stand(VillageResidentPresentation actor, Vector3 dock, Vector3 forward,
            float seconds, Vector3? look = null, Vector3? up = null)
        {
            Vector3 normal = up ?? Vector3.up;
            forward = Vector3.ProjectOnPlane(forward, normal).normalized;
            actor.transform.SetPositionAndRotation(dock, Quaternion.LookRotation(forward, normal));
            actor.Apply(VillageResidentAction.Idle, seconds);
            if (look.HasValue) ApplyTaskLook(actor, look.Value);
        }

        private void Tint(int index)
        {
            for (int j = 0; j < clothing[index].Length; j++)
            {
                var renderer = clothing[index][j];
                renderer.GetPropertyBlock(block);
                string part = renderer.name;
                Color tint = IsGlove(part) ? gloveTints[index] :
                    part.Contains("Cap") || part.Contains("Scarf") || part.Contains("Collar") || part.Contains("Cuff") ?
                    knitTints[index] : part.Contains("Thigh") || part.Contains("Shin") ?
                    new Color(.85f, .88f, .90f) : clothingTints[index];
                Color color = originalColors[index][j] * tint;
                color.a = 1f;
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                if (IsFabric(part))
                {
                    // Presentation reapplies its shared atlas on enable. Only
                    // these five workers replace the authored fabric cells;
                    // skin, faces, gloves and rubber boots retain that atlas.
                    block.SetTexture("_BaseMap", fabricTexture);
                    block.SetTexture("_MainTex", fabricTexture);
                    block.SetVector("_BaseMap_ST", fabricUvTransforms[index][j]);
                }
                renderer.SetPropertyBlock(block);
                block.Clear();
            }
        }

        private void ApplyPlantedTorso(int index, float leanDegrees, float rollDegrees, double seconds)
        {
            // Apply only after the absolute authored pose, before both hand
            // contacts. The spine cannot move pelvis, knees or planted feet.
            var actor = workers[index];
            float roll = (float)Math.Sin(seconds * .63d + index * 1.7d) * rollDegrees;
            spines[index].rotation = Quaternion.AngleAxis(roll, actor.transform.forward) *
                Quaternion.AngleAxis(leanDegrees, actor.transform.right) * spines[index].rotation;
        }

        private static void ApplyTaskLook(VillageResidentPresentation actor, Vector3 target, float weight = 1f)
        {
            Vector3 direction = actor.transform.InverseTransformDirection(target - actor.Head.position);
            if (direction.sqrMagnitude < .001f) return;
            float yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -32f, 32f);
            float pitch = Mathf.Clamp(-Mathf.Atan2(direction.y,
                new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -18f, 18f);
            actor.Head.rotation = Quaternion.AngleAxis(yaw * weight, actor.transform.up) *
                Quaternion.AngleAxis(pitch * weight, actor.transform.right) * actor.Head.rotation;
        }

        private static bool IsGlove(string name) => name.StartsWith("GEO_Hand.", StringComparison.Ordinal) ||
            name.StartsWith("GEO_Thumb.", StringComparison.Ordinal);

        private static bool IsFabric(string name) => name.StartsWith("CLO_", StringComparison.Ordinal) &&
            !name.StartsWith("CLO_Shin", StringComparison.Ordinal);

        private static Vector4 FabricUvTransform(Renderer renderer)
        {
            // StationWorker's Blender generator maps every fabric part from
            // its rest X/Y bounds into one declared atlas cell. Undo that
            // packing, retaining its authored projection at one repeat/metre.
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh :
                renderer.GetComponent<MeshFilter>().sharedMesh;
            Vector3 size = mesh.bounds.size;
            float width = size.x * Mathf.Abs(renderer.transform.lossyScale.x);
            float height = size.y * Mathf.Abs(renderer.transform.lossyScale.y);
            bool coat = renderer.name == "CLO_WinterCoat";
            bool knit = renderer.name.Contains("Cap") || renderer.name.Contains("Scarf") ||
                renderer.name.Contains("Collar") || renderer.name.Contains("Cuff");
            float cellX = coat ? 2f : 130f, cellY = coat || knit ? 130f : 2f;
            float cellWidth = coat || knit ? 123f : 60f;
            float u = Mathf.Max(.01f, width) * 256f / cellWidth;
            float v = Mathf.Max(.01f, height) * 256f / 123f;
            return new Vector4(u, v, -cellX / 256f * u, -cellY / 256f * v);
        }

        private static void SetVisible(VillageResidentPresentation actor, bool visible)
        {
            if (actor.gameObject.activeSelf != visible) actor.gameObject.SetActive(visible);
        }
        private static Transform Require(Transform parent, string name) =>
            CityPedestrianHandProps.FindSocket(parent, name) ??
            throw new InvalidOperationException("Missing port worker contact " + name);
        private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
        private static float EaseWindow(float seconds, float duration, float fade) =>
            Smooth(seconds / fade) * Smooth((duration - seconds) / fade);
    }
}
