using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Reconstructs the complete harbour visit from the session clock. Imported
    /// pivots own the moving geometry; one finite set of cages changes custody
    /// from the hold to a hook, the trolley and the enclosed cold store.
    /// </summary>
    public sealed partial class CityPortController : MonoBehaviour
    {
        private const float HookClearanceHeight = 8f;
        private const int MooringSegments = 6;
        private readonly Transform[] hatches = new Transform[2];
        private readonly Quaternion[] hatchClosed = new Quaternion[2];
        private readonly Transform[] leftLevers = new Transform[2];
        private readonly Transform[] rightLevers = new Transform[2];
        private readonly Vector3[] hookLoadOffsets = new Vector3[2];
        private readonly Vector3[] cargoLiftOffsets = new Vector3[CityPortCycle.CargoCount];
        private readonly Transform[] hoistRopes = new Transform[2];
        private readonly Transform[] craneHeads = new Transform[2];
        private readonly Transform[] headFeeds = new Transform[2];
        private readonly Transform[] boomHeels = new Transform[2];
        private readonly Transform[] winchRopes = new Transform[2];
        private readonly Transform[,] mooringRopes = new Transform[4, MooringSegments];
        private readonly Transform[] vesselMooring = new Transform[2];
        private readonly Transform[] dockMooring = new Transform[2];
        private Vector3 trolleyLoadOffset;
        private Vector3 trolleyOperatorOffset;
        private Collider trolleyCollider;
        private readonly Vector3[][] trolleyRoutes = new Vector3[2][];
        private readonly float[][] trolleyTurnCosts = new float[2][];
        private readonly float[] trolleyRouteLengths = new float[2];
        private CityWaterWaveProfile waves;
        private float lastWaveTime;

        public CityPortPlan Plan { get; private set; }
        public CityPortCycleSnapshot Snapshot { get; private set; }
        public Transform Dock { get; private set; }
        public Transform Vessel { get; private set; }
        public Transform[] CraneBases { get; private set; }
        public Transform[] Booms { get; private set; }
        public Transform[] Hooks { get; private set; }
        public Transform[] Cargo { get; private set; }
        public Transform Trolley { get; private set; }
        public float TrolleySpeed { get; private set; }
        public float TrolleyOperatorSpeed { get; private set; }
        public bool IsTrolleyReversing => Snapshot.CargoStage == CityPortCargoStage.Return;
        public double ElapsedSeconds { get; private set; }
        public bool AutoAdvance { get; set; } = true;

        public static CityPortController Build(Transform parent, CityPortPlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var root = new GameObject("Working Fishing Port");
            root.transform.SetParent(parent, false);
            var controller = root.AddComponent<CityPortController>();
            controller.Initialize(plan);
            return controller;
        }

        private void Initialize(CityPortPlan plan)
        {
            Plan = plan;
            Dock = Create("Dock");
            Dock.position = plan.Origin;
            Vessel = Create("Trawler");
            Trolley = Create("Trolley");
            trolleyCollider = Trolley.GetComponent<Collider>();
            trolleyLoadOffset = AnchorOffset(Trolley, "ANCHOR_Load");
            trolleyOperatorOffset = AnchorOffset(Trolley, "ANCHOR_Handle") - Vector3.forward * .48f;
            trolleyOperatorOffset.y = 0f;
            for (int crane = 0; crane < 2; crane++)
            {
                trolleyRoutes[crane] = new[]
                {
                    plan.LandingLocal(crane),
                    new Vector3(0f, CityPortPlan.DeckHeight, -10.5f),
                    new Vector3(0f, CityPortPlan.DeckHeight, -14f),
                    new Vector3(-2.3f, CityPortPlan.DeckHeight, -14f),
                    new Vector3(-2.3f, CityPortPlan.DeckHeight, -17.5f),
                    plan.WarehouseDropLocal
                };
                for (int segment = 1; segment < trolleyRoutes[crane].Length; segment++)
                    trolleyRouteLengths[crane] += Vector3.Distance(trolleyRoutes[crane][segment - 1],
                        trolleyRoutes[crane][segment]);
                trolleyTurnCosts[crane] = new float[trolleyRoutes[crane].Length - 2];
                for (int corner = 1; corner < trolleyRoutes[crane].Length - 1; corner++)
                {
                    Vector3 before = trolleyRoutes[crane][corner] - trolleyRoutes[crane][corner - 1];
                    Vector3 after = trolleyRoutes[crane][corner + 1] - trolleyRoutes[crane][corner];
                    float cost = Vector3.Angle(before, after) * Mathf.Deg2Rad * trolleyOperatorOffset.magnitude;
                    trolleyTurnCosts[crane][corner - 1] = cost;
                    trolleyRouteLengths[crane] += cost;
                }
            }
            CraneBases = new Transform[2];
            Booms = new Transform[2];
            Hooks = new Transform[2];
            for (int crane = 0; crane < 2; crane++)
            {
                CraneBases[crane] = Create("CraneBase");
                CraneBases[crane].position = plan.World(plan.CraneBaseLocal(crane));
                leftLevers[crane] = Part(CraneBases[crane], "LeverLeft");
                rightLevers[crane] = Part(CraneBases[crane], "LeverRight");
                Booms[crane] = Create("CraneBoom");
                craneHeads[crane] = Part(CraneBases[crane], "CraneHead");
                headFeeds[crane] = Part(CraneBases[crane], "ANCHOR_HoistFeed");
                boomHeels[crane] = Part(Booms[crane], "ANCHOR_Heel");
                winchRopes[crane] = Create("RopeSegment");
                Hooks[crane] = Create("Hook");
                hookLoadOffsets[crane] = AnchorOffset(Hooks[crane], "ANCHOR_Load");
                hoistRopes[crane] = Create("RopeSegment");
                string hatchName = crane == 0 ? "HatchB" : "HatchA";
                hatches[crane] = Part(Vessel, hatchName);
                hatchClosed[crane] = hatches[crane].localRotation;
                string mooringName = crane == 0 ? "ANCHOR_MoorBow" : "ANCHOR_MoorStern";
                vesselMooring[crane] = Part(Vessel, mooringName);
                dockMooring[crane] = Part(Dock, mooringName);
            }
            for (int line = 0; line < 4; line++)
            for (int segment = 0; segment < MooringSegments; segment++)
                mooringRopes[line, segment] = Create("RopeSegment");

            Cargo = new Transform[CityPortCycle.CargoCount];
            for (int index = 0; index < Cargo.Length; index++)
            {
                Cargo[index] = Create("Cargo");
                Cargo[index].name = "Port Cargo " + index.ToString("D2");
                cargoLiftOffsets[index] = AnchorOffset(Cargo[index], "ANCHOR_Lift");
            }
            waves = CitySeaResources.CreateWaveProfile();
            ApplyAt(SessionSeconds, Time.timeSinceLevelLoad);
        }

        private Transform Create(string name) =>
            CityPortAssetProvider.Create(name, transform).transform;

        private static Transform Part(Transform root, string name) =>
            CityPortAssetProvider.FindPart(root.gameObject, name);

        private static Vector3 AnchorOffset(Transform root, string name) =>
            root.InverseTransformPoint(Part(root, name).position);

        private static double SessionSeconds =>
            (GameSessionState.GameDayIndex * 1440d +
             GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond;

        public bool IsSupplyDriven { get; set; }

        private void Update()
        {
            if (!AutoAdvance) { RefreshPresentation(); return; }
            if (GameSessionState.IsGameTimeRunning && !GameTimeScaleRuntime.IsPaused)
                lastWaveTime = Time.timeSinceLevelLoad;
            ApplyAt(SessionSeconds, lastWaveTime);
        }

        public void ApplyAt(double seconds, float waveTime)
        {
            Snapshot = CityPortCycle.Sample(seconds);
            ElapsedSeconds = seconds;
            lastWaveTime = waveTime;
            UpdatePresentationVisibility();
            ApplyPresentation();
        }

        /// <summary>The finite set of cages fits below the two open hatch apertures.</summary>
        public static Vector3 CargoHoldLocal(int index)
        {
            if (index < 0 || index >= CityPortCycle.CargoCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new Vector3((index / 2 - 1) * 1.35f, -.5f, index % 2 == 0 ? 4f : -4f);
        }

        private void ApplyVessel(float waveTime)
        {
            float approach = 1f;
            Quaternion rotation;
            if (Snapshot.Stage == CityPortCycleStage.Approach)
            {
                approach = Ease(Snapshot.StageProgress);
                rotation = Plan.SampleVesselRotation(approach);
            }
            else if (Snapshot.Stage == CityPortCycleStage.Depart)
            {
                float depart = Ease(Snapshot.StageProgress);
                approach = 1f - depart;
                rotation = Plan.SampleDepartureRotation(depart);
            }
            else if (Snapshot.Stage == CityPortCycleStage.Idle)
            {
                approach = 0f;
                rotation = Plan.SampleVesselRotation(0f);
            }
            else rotation = Plan.SampleVesselRotation(1f);

            Vector3 position = Plan.World(Plan.SampleVesselPosition(approach));
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            float fore = Height(position + forward * 6f, waveTime);
            float aft = Height(position - forward * 6f, waveTime);
            float port = Height(position - right * 2f, waveTime);
            float starboard = Height(position + right * 2f, waveTime);
            float seaMotion = 1f - Ease(Mathf.InverseLerp(.75f, 1f, approach));
            // Lines hold the berthed hull level, with only a small vertical give.
            position.y += (fore + aft + port + starboard) * .25f * Mathf.Lerp(.08f, 1f, seaMotion);
            float pitch = -Mathf.Atan2(fore - aft, 12f) * Mathf.Rad2Deg * seaMotion;
            float roll = Mathf.Atan2(starboard - port, 4f) * Mathf.Rad2Deg * seaMotion;
            Vessel.SetPositionAndRotation(position, rotation * Quaternion.Euler(pitch, 0f, roll));
            Vessel.gameObject.SetActive(Snapshot.VesselPresent);
        }

        private float Height(Vector3 position, float time) =>
            CityWaterWaveModel.SampleHeight(waves, position.x, position.z, time);

        private void ApplyHatches()
        {
            float open = 0f;
            if (Snapshot.Stage == CityPortCycleStage.Prepare) open = Ease(Snapshot.StageProgress);
            else if (Snapshot.Stage == CityPortCycleStage.Unload) open = 1f;
            else if (Snapshot.Stage == CityPortCycleStage.Secure) open = 1f - Ease(Snapshot.StageProgress);
            for (int index = 0; index < hatches.Length; index++)
                hatches[index].localRotation = hatchClosed[index] * Quaternion.Euler(0f, 0f, -95f * open);
        }

        private void ApplyTrolley()
        {
            int crane = 0;
            float progress = 0f;
            double motionDuration = 0d;
            if (Snapshot.Stage == CityPortCycleStage.Unload)
            {
                crane = Snapshot.ActiveCraneIndex;
                double t = Snapshot.SecondsInCargo;
                if (t >= CityPortCycle.StoredAtSeconds)
                {
                    // After the last load, park at the first crane for the
                    // next visit, including visits with an odd load count.
                    crane = Snapshot.CargoIndex + 1 < CityPortCycle.CargoCount ? (crane + 1) % 2 : 0;
                    motionDuration = CityPortCycle.CargoDurationSeconds - CityPortCycle.StoredAtSeconds;
                    progress = 1f - (float)((t - CityPortCycle.StoredAtSeconds) / motionDuration);
                }
                else if (t >= CityPortCycle.UnhookedAtSeconds)
                {
                    motionDuration = CityPortCycle.StoredAtSeconds - CityPortCycle.UnhookedAtSeconds;
                    progress = (float)((t - CityPortCycle.UnhookedAtSeconds) / motionDuration);
                }
            }
            Trolley.SetPositionAndRotation(TrolleyPosition(crane, progress), TrolleyRotation(crane, progress));
            TrolleySpeed = 0f;
            TrolleyOperatorSpeed = 0f;
            if (motionDuration > 0d)
            {
                float offset = .01f / (float)motionDuration;
                TrolleySpeed = Vector3.Distance(TrolleyPosition(crane, progress - offset),
                    TrolleyPosition(crane, progress + offset)) / .02f;
                Vector3 before = TrolleyPosition(crane, progress - offset) +
                    TrolleyRotation(crane, progress - offset) * trolleyOperatorOffset;
                Vector3 after = TrolleyPosition(crane, progress + offset) +
                    TrolleyRotation(crane, progress + offset) * trolleyOperatorOffset;
                TrolleyOperatorSpeed = Vector3.Distance(before, after) / .02f;
            }
        }

        private Vector3 TrolleyPosition(int crane, float progress)
        {
            SampleTrolleyRoute(crane, progress, out Vector3 position, out _);
            return position;
        }

        private Quaternion TrolleyRotation(int crane, float progress)
        {
            // An empty cart is pulled backward; its arrival orientation is
            // already the orientation needed by the next loaded departure.
            SampleTrolleyRoute(crane, progress, out _, out Quaternion rotation);
            return rotation;
        }

        private void SampleTrolleyRoute(int crane, float progress, out Vector3 position, out Quaternion rotation)
        {
            Vector3[] points = trolleyRoutes[crane];
            float remaining = Mathf.Clamp01(progress) * trolleyRouteLengths[crane];
            for (int segment = 1; segment < points.Length; segment++)
            {
                Vector3 delta = points[segment] - points[segment - 1];
                float length = delta.magnitude;
                Quaternion heading = Quaternion.LookRotation(delta, Vector3.up);
                if (remaining <= length || segment == points.Length - 1)
                {
                    position = Plan.World(Vector3.Lerp(points[segment - 1], points[segment], Ease(remaining / length)));
                    rotation = heading;
                    return;
                }
                remaining -= length;
                Quaternion nextHeading = Quaternion.LookRotation(points[segment + 1] - points[segment], Vector3.up);
                float turnCost = trolleyTurnCosts[crane][segment - 1];
                if (remaining <= turnCost && turnCost > .0001f)
                {
                    position = Plan.World(points[segment]);
                    rotation = Quaternion.Slerp(heading, nextHeading, Ease(remaining / turnCost));
                    return;
                }
                remaining -= turnCost;
            }
            position = Plan.World(points[points.Length - 1]);
            rotation = Quaternion.LookRotation(points[points.Length - 1] - points[points.Length - 2]);
        }

        private Vector3 BerthCargoPosition(int index) => Plan.World(Plan.VesselBerthLocal +
            Plan.SampleVesselRotation(1f) * CargoHoldLocal(index));

        private Vector3 ParkedHook(int index)
        {
            Vector3 position = BerthCargoPosition(index);
            position.y = Plan.Origin.y + HookClearanceHeight;
            return position;
        }

        private Vector3 RaisedLanding(int crane)
        {
            Vector3 position = Plan.World(Plan.LandingLocal(crane));
            position.y = Plan.Origin.y + HookClearanceHeight;
            return position;
        }

        private Vector3 HookContact(int crane)
        {
            int cargo = Snapshot.CargoIndex;
            if (Snapshot.Stage != CityPortCycleStage.Unload) return ParkedHook(crane);
            if (Snapshot.ActiveCraneIndex != crane)
            {
                int next = cargo + 1;
                return ParkedHook(next < CityPortCycle.CargoCount ? next : crane);
            }

            Vector3 raisedPick = ParkedHook(cargo);
            Vector3 pick = Vessel.TransformPoint(CargoHoldLocal(cargo)) +
                Vessel.rotation * cargoLiftOffsets[cargo];
            Vector3 raisedLanding = RaisedLanding(crane);
            Vector3 landing = Plan.World(Plan.LandingLocal(crane)) +
                TrolleyRotation(crane, 0f) * (trolleyLoadOffset + cargoLiftOffsets[cargo]);
            float t = Ease(Snapshot.CargoStageProgress);
            switch (Snapshot.CargoStage)
            {
                case CityPortCargoStage.LowerHook: return Vector3.Lerp(raisedPick, pick, t);
                case CityPortCargoStage.Hoist: return Vector3.Lerp(pick, raisedPick, t);
                case CityPortCargoStage.Slew: return Slew(crane, raisedPick, raisedLanding, t);
                case CityPortCargoStage.LowerLoad: return Vector3.Lerp(raisedLanding, landing, t);
                case CityPortCargoStage.Unhook:
                    return Vector3.Lerp(landing, raisedLanding,
                        Ease((float)((Snapshot.SecondsInCargo - 26d) / 2d)));
                case CityPortCargoStage.Trolley: return raisedLanding;
                case CityPortCargoStage.Return:
                    int next = cargo + 2;
                    return Slew(crane, ParkedHook(next < CityPortCycle.CargoCount ? next : crane),
                        raisedLanding, 1f - t);
                default: return raisedPick;
            }
        }

        private Vector3 Slew(int crane, Vector3 pick, Vector3 landing, float progress)
        {
            Vector3 center = Plan.World(Plan.CraneBaseLocal(crane));
            float radius = Mathf.Lerp(Mathf.Abs(pick.z - center.z),
                Mathf.Abs(landing.z - center.z), progress);
            float angle = (crane == 0 ? -1f : 1f) * Mathf.PI * progress;
            return new Vector3(center.x + Mathf.Sin(angle) * radius,
                Plan.Origin.y + HookClearanceHeight, center.z + Mathf.Cos(angle) * radius);
        }

        private void ApplyCrane(int crane)
        {
            Vector3 contact = HookContact(crane);
            Hooks[crane].SetPositionAndRotation(SuspendedContact(crane) - hookLoadOffsets[crane], Quaternion.identity);
            Vector3 pivot = Plan.World(Plan.CraneBaseLocal(crane) + Vector3.up * 4f);
            Vector3 reach = contact - pivot;
            reach.y = 0f;
            float height = Mathf.Sqrt(Mathf.Max(.01f,
                CityPortPlan.BoomLength * CityPortPlan.BoomLength - reach.sqrMagnitude));
            Vector3 tip = pivot + reach + Vector3.up * height;
            craneHeads[crane].rotation = Quaternion.LookRotation(reach.normalized, Vector3.up);
            Booms[crane].SetPositionAndRotation(pivot, Quaternion.LookRotation(tip - pivot, Vector3.up));
            SetSegment(winchRopes[crane], headFeeds[crane].position, boomHeels[crane].position);
            SetSegment(hoistRopes[crane], tip, Hooks[crane].position);
            // The imported grip anchors are children of these levers. Crew
            // contacts sample them after actuation, including the return stroke.
            float hoist = 0f, slew = 0f;
            if (Snapshot.ActiveCraneIndex == crane)
            {
                float pulse = Mathf.Sin(Mathf.PI * Snapshot.CargoStageProgress);
                switch (Snapshot.CargoStage)
                {
                    case CityPortCargoStage.LowerHook: hoist = 14f * pulse; break;
                    case CityPortCargoStage.Hoist: hoist = -18f * pulse; break;
                    case CityPortCargoStage.Slew: slew = (crane == 0 ? -18f : 18f) * pulse; break;
                    case CityPortCargoStage.LowerLoad: hoist = 16f * pulse; break;
                    case CityPortCargoStage.Return: hoist = -12f * pulse; slew = (crane == 0 ? 14f : -14f) * pulse; break;
                }
            }
            leftLevers[crane].localRotation = Quaternion.Euler(hoist, 0f, 0f);
            rightLevers[crane].localRotation = Quaternion.Euler(slew, 0f, 0f);
        }

        private Vector3 SuspendedContact(int crane)
        {
            Vector3 contact = HookContact(crane);
            if (Snapshot.ActiveCraneIndex != crane || Snapshot.CargoStage != CityPortCargoStage.Slew)
                return contact;
            // A small cable swing follows the slew and settles completely
            // before lowering. Absolute sampling preserves pause/seek and
            // the exact hook-to-cage contact without a physics simulation.
            float t = Snapshot.CargoStageProgress;
            float envelope = Mathf.Sin(Mathf.PI * t);
            float swing = .09f * envelope * envelope * Mathf.Sin(3f * Mathf.PI * t);
            Vector3 radial = contact - Plan.World(Plan.CraneBaseLocal(crane));
            radial.y = 0f;
            return contact + Vector3.Cross(Vector3.up, radial.normalized) * swing;
        }

        private void ApplyCargo()
        {
            // The cart's motion sleeps with its distant presentation. Disable
            // its body in that state; approach applies the current pose before
            // restoring collision. Cargo uses its own finite active hosts.
            trolleyCollider.enabled = ShorePresentationActive;
            bool unloading = Snapshot.Stage == CityPortCycleStage.Unload;
            bool completed = (int)Snapshot.Stage > (int)CityPortCycleStage.Unload;
            for (int index = 0; index < Cargo.Length; index++)
            {
                bool stored = completed || (unloading && (index < Snapshot.CargoIndex ||
                    (index == Snapshot.CargoIndex && Snapshot.SecondsInCargo >= CityPortCycle.StoredAtSeconds)));
                // The delivery point is inside the opaque cold store. A cage
                // leaves presentation only there; its count remains in Snapshot.
                if (stored)
                {
                    if (ShorePresentationActive) Cargo[index].SetPositionAndRotation(
                        Plan.World(Plan.WarehouseDropLocal) + Vector3.up * trolleyLoadOffset.y,
                        TrolleyRotation(index % 2, 1f));
                    Cargo[index].gameObject.SetActive(false);
                    continue;
                }
                bool onShore = unloading && index == Snapshot.CargoIndex &&
                    Snapshot.SecondsInCargo >= CityPortCycle.HookedAtSeconds;
                bool visible = Snapshot.VesselPresent && (onShore ? ShorePresentationActive : VesselPresentationActive);
                Cargo[index].gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 position = Vessel.TransformPoint(CargoHoldLocal(index));
                Quaternion rotation = Vessel.rotation;
                if (unloading && index == Snapshot.CargoIndex &&
                    Snapshot.SecondsInCargo >= CityPortCycle.HookedAtSeconds)
                {
                    int crane = Snapshot.ActiveCraneIndex;
                    Quaternion landingRotation = TrolleyRotation(crane, 0f);
                    if (Snapshot.SecondsInCargo >= CityPortCycle.LandedAtSeconds)
                    {
                        rotation = Trolley.rotation;
                        position = Trolley.TransformPoint(trolleyLoadOffset);
                    }
                    else
                    {
                        if (Snapshot.CargoStage == CityPortCargoStage.Slew)
                            rotation = Quaternion.Slerp(Vessel.rotation, landingRotation,
                                Ease(Snapshot.CargoStageProgress));
                        else if (Snapshot.CargoStage == CityPortCargoStage.LowerLoad)
                            rotation = landingRotation;
                        position = SuspendedContact(crane) - rotation * cargoLiftOffsets[index];
                    }
                }
                Cargo[index].SetPositionAndRotation(position, rotation);
            }
        }

        public float MooringProgress(int line)
        {
            if (line < 0 || line >= 4) throw new ArgumentOutOfRangeException(nameof(line));
            int dock = line < 2 ? line : 1 - line % 2;
            if (Snapshot.Stage == CityPortCycleStage.Moor)
                return Ease(Mathf.InverseLerp(dock == 0 ? 2f : 23f,
                    dock == 0 ? 4f : 25f, (float)Snapshot.SecondsInStage));
            if (Snapshot.Stage == CityPortCycleStage.Unmoor)
                return 1f - Ease(Mathf.InverseLerp(dock == 0 ? 18f : 1.5f,
                    dock == 0 ? 19.5f : 3f, (float)Snapshot.SecondsInStage));
            return (int)Snapshot.Stage >= (int)CityPortCycleStage.Prepare &&
                (int)Snapshot.Stage <= (int)CityPortCycleStage.Secure ? 1f : 0f;
        }

        private void ApplyMoorings()
        {
            for (int line = 0; line < 4; line++)
            {
                float progress = MooringProgress(line);
                int ship = line % 2;
                int dock = line < 2 ? ship : 1 - ship;
                Vector3 start = dockMooring[dock].position;
                Vector3 end = Vector3.Lerp(start, vesselMooring[ship].position, progress);
                Vector3 previous = start;
                for (int segment = 0; segment < MooringSegments; segment++)
                {
                    float t = (segment + 1f) / MooringSegments;
                    Vector3 next = Vector3.Lerp(start, end, t) -
                        Vector3.up * (Mathf.Sin(t * Mathf.PI) * .18f * progress);
                    SetSegment(mooringRopes[line, segment], previous, next);
                    previous = next;
                }
            }
        }

        private static void SetSegment(Transform segment, Vector3 start, Vector3 end)
        {
            Vector3 direction = end - start;
            float length = direction.magnitude;
            segment.gameObject.SetActive(length > .005f);
            if (length <= .005f) return;
            segment.SetPositionAndRotation(start, Quaternion.LookRotation(direction, Vector3.up));
            segment.localScale = new Vector3(1f, 1f, length);
        }

        private static float Ease(float progress) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
    }
}
