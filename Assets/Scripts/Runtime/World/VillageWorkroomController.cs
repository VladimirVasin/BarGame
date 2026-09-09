using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The two residents use the same tools and furniture that a visitor sees through the windows.</summary>
    public sealed class VillageWorkroomController : MonoBehaviour
    {
        private sealed class Work
        {
            internal bool Active;
            internal int LastOuting = -1, Cycles;
            internal float Time;
            internal VillageResidentAction Action;
        }

        private readonly Work repair = new Work(), sewing = new Work();
        private AlpineVillageRoot village;
        private VillageLifeAudio sounds;
        private Transform hammer, mitten, cloth, box, lid, flap, rail, thread;
        private Quaternion lidClosed, flapOpen;
        private bool helpReserved, helperHolding, assistedStrokeDone, assistedStrokeStarted;
        private float assistedSpeed = 1f;
        private float repairProgress;
        public VillageWorkroomInstance Room { get; private set; }
        public VillageWorkroomPlan Plan => Room.Plan;
        public VillageWorkroomEnvironment Environment { get; private set; }
        public VillageWorkroomCameraController CameraController { get; private set; }
        public VillageWorkroomPlayerInteractions Help { get; private set; }
        public VillageWorkroomDoorInteraction Door { get; private set; }
        public CityBenchSitInteraction Bench { get; private set; }
        public bool ChairFixed => GameSessionState.VillageHousehold.ChairFixed;
        public int SewingCycles { get; private set; }
        public int RepairStrikes { get; private set; }
        public bool HelpReserved => helpReserved;
        public VillageResidentAction RepairAction => repair.Action;
        public VillageResidentAction SewingAction => sewing.Action;
        public float RepairTime => repair.Time;
        public float SewingTime => sewing.Time;
        public bool RepairActive => repair.Active;
        public bool SewingActive => sewing.Active;
        public Transform RepairRail => rail;

        public static VillageWorkroomController Create(AlpineVillageRoot owner)
        {
            var host = new GameObject("House 08 Daily Work");
            host.transform.SetParent(owner.transform, false);
            var result = host.AddComponent<VillageWorkroomController>();
            result.Initialize(owner);
            return result;
        }

        private void Initialize(AlpineVillageRoot owner)
        {
            village = owner; Room = owner.World.Workroom;
            if (Room == null) throw new InvalidOperationException("The village workroom must be built before its residents begin work.");
            hammer = Part("Hammer"); mitten = Part("Mitten"); cloth = Part("Cloth");
            box = Part("Box"); lid = Part("BoxLid"); flap = Part("ClothFlap"); rail = Part("RepairRail");
            thread = Part("Thread");
            if (ChairFixed) SetRepairProgress(1f);
            lidClosed = Quaternion.identity; flapOpen = Quaternion.identity;
            sounds = CreateAudio();
            PlaceProps(VillageResidentAction.RepairTakeTool, 0f, Plan.Anchor("RepairDock"), Quaternion.LookRotation(Plan.Facing("RepairDock")));
            PlaceProps(VillageResidentAction.SewingEnter, 0f, Plan.Anchor("SewingDock"), Quaternion.LookRotation(Plan.Facing("SewingDock")));
            var shared = owner.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            Help = Trigger("Hold chair rail", Plan.Anchor("PlayerHelpDock") + Vector3.up * .85f, .85f)
                .AddComponent<VillageWorkroomPlayerInteractions>();
            Help.Initialize(owner.Player, shared, PlayerDoorActionPlan.CreateStationary(
                Plan.Anchor("ChairPartRightGrip"), Plan.Anchor("PlayerHelpDock") + Vector3.up * PlayerFactory.GroundedRootOffset,
                Plan.Facing("PlayerHelpDock")), CanHelp, ReserveHelp, FinishHelp,
                RailGrip("ChairPartLeftGrip"), RailGrip("ChairPartRightGrip"));
            Help.HoldStarted += StartAssistedStroke;
            var seat = new CityBenchSeat("village-workroom-bench", Plan.Anchor("BenchSeat"),
                VillageWorkroomPlan.BenchWidth, VillageWorkroomPlan.BenchDepth, Plan.House.GroundCenter.y,
                Plan.Facing("BenchSeat"), frontApproachOnly: true);
            Bench = VillageWorkroomPlayerInteractions.InstallBench(
                Trigger("Workroom bench", Plan.Anchor("BenchApproach") + Vector3.up * .7f, .8f), owner.Player, shared, seat);
            var physicalDoor = owner.World.ResidentDoors[VillageWorkroomPlan.HouseId];
            Door = Trigger("Workroom door", physicalDoor.ThresholdDock + Vector3.up, 1.7f)
                .AddComponent<VillageWorkroomDoorInteraction>();
            Door.Initialize(owner.Player, shared, physicalDoor);
            Environment = gameObject.AddComponent<VillageWorkroomEnvironment>();
            Environment.Initialize(owner, Room, sounds);
            CameraController = gameObject.AddComponent<VillageWorkroomCameraController>();
            CameraController.Initialize(owner.CameraFollow, owner.Player.GameObject.transform, Plan);
            owner.Life.AttachWorkroom(this);
        }

        private VillageLifeAudio CreateAudio()
        {
            var audio = gameObject.AddComponent<VillageLifeAudio>();
            audio.Initialize();
            return audio;
        }

        private Transform Part(string name) => Room.Parts.TryGetValue(name, out Transform part) ? part :
            throw new InvalidOperationException("The authored workroom is missing " + name);

        private Transform RailGrip(string anchor)
        {
            var grip = new GameObject(anchor).transform;
            grip.SetParent(rail, false); grip.position = Plan.Anchor(anchor);
            return grip;
        }

        private GameObject Trigger(string name, Vector3 position, float radius)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false); host.transform.position = position;
            var collider = host.AddComponent<SphereCollider>(); collider.isTrigger = true; collider.radius = radius;
            return host;
        }

        private Work State(VillageResidentRole role) => role == VillageResidentRole.RepairNeighbor ? repair :
            role == VillageResidentRole.SewingWoman ? sewing : null;

        public bool WantsIndoorWork(VillageNeighbourState resident)
        {
            Work work = State(resident.Role);
            return work != null && work.LastOuting != resident.CompletedOutings;
        }

        public void ReserveIndoorWork(VillageNeighbourState resident)
        {
            Work work = State(resident.Role);
            work.LastOuting = resident.CompletedOutings; work.Active = false; work.Cycles = 0; work.Time = 0f;
        }

        public bool AdvanceResident(VillageNeighbourState resident, float dt, bool day)
        {
            Work work = State(resident.Role);
            if (work == null) return true;
            bool isRepair = work == repair;
            if (!work.Active)
            {
                work.Active = true;
                SetAction(work, isRepair ? (ChairFixed ? VillageResidentAction.Idle : VillageResidentAction.RepairTakeTool) : VillageResidentAction.SewingEnter);
            }
            float before = work.Time;
            bool waitingForHelper = isRepair && helpReserved && work.Action == VillageResidentAction.RepairWork &&
                ((!helperHolding && work.Time >= 4f) || assistedStrokeDone);
            if (!waitingForHelper) work.Time += dt * (isRepair && assistedStrokeStarted && !assistedStrokeDone ? assistedSpeed : 1f);
            if (work.Action == VillageResidentAction.Idle)
            {
                resident.Actor.Apply(work.Action, work.Time);
                if (!day || work.Time >= 30f) { work.Active = false; return true; }
                return false;
            }
            float duration = Duration(work.Action);
            float sampleTime = Mathf.Min(work.Time, duration);
            resident.Actor.ApplyWorkroom(work.Action, sampleTime);
            VillageWorkroomFrame frame = PlaceProps(work.Action, sampleTime, resident.Actor.transform.position, resident.Actor.transform.rotation);
            if (isRepair && work.Action == VillageResidentAction.RepairWork && !waitingForHelper)
            {
                foreach (float strike in VillageResidentPresentation.RepairStrikeSeconds)
                    if (before < strike && work.Time >= strike)
                    {
                        RepairStrikes++; sounds.PlayWood(Plan.Anchor("RepairJoin"), .48f);
                        if (assistedStrokeStarted)
                        {
                            int beat = Array.IndexOf(VillageResidentPresentation.RepairStrikeSeconds, strike) + 1;
                            SetRepairProgress(Mathf.Max(repairProgress, beat / 3f));
                            if (beat == 3) FixChair();
                        }
                        else if (!helpReserved) SetRepairProgress((work.Cycles + strike / 4f) / 24f);
                    }
            }
            if (!isRepair && work.Action == VillageResidentAction.SewingWork && before < 1.5f && work.Time >= 1.5f)
                sounds.PlayCloth(mitten.position);
            if (isRepair && frame.LeftContactKind == VillageWorkroomContact.Chair && frame.LeftContactWeight > 0f)
                resident.Actor.ApplyHandContacts(null, resident.Actor.LeftGrip.position +
                    (rail.position - Plan.Anchor("RepairRailRest")) * frame.LeftContactWeight, 1f);
            if (work.Time < duration) return false;
            if (isRepair)
            {
                if (work.Action == VillageResidentAction.RepairTakeTool) SetAction(work, VillageResidentAction.RepairWork);
                else if (work.Action == VillageResidentAction.RepairWork)
                {
                    if (helpReserved)
                    {
                        if (assistedStrokeStarted) assistedStrokeDone = true;
                        else if (helperHolding) BeginAssistedCycle();
                        // Finish the current stroke and hold both objects still
                        // until the guest's entry / held cycle / exit is visible.
                        return false;
                    }
                    work.Cycles++;
                    if (!day || work.Cycles >= 24 || ChairFixed)
                    {
                        if (work.Cycles >= 24) FixChair();
                        SetAction(work, VillageResidentAction.RepairPutTool);
                    }
                    else SetAction(work, VillageResidentAction.RepairWork);
                }
                else { work.Active = false; return true; }
            }
            else
            {
                switch (work.Action)
                {
                    case VillageResidentAction.SewingEnter: SetAction(work, VillageResidentAction.SewingUnpack); break;
                    case VillageResidentAction.SewingUnpack: SetAction(work, VillageResidentAction.SewingWork); break;
                    case VillageResidentAction.SewingWork:
                        work.Cycles++;
                        SetAction(work, day && work.Cycles % 4 != 0 ? VillageResidentAction.SewingWork : VillageResidentAction.SewingFold); break;
                    case VillageResidentAction.SewingFold: SetAction(work, VillageResidentAction.SewingStow); break;
                    case VillageResidentAction.SewingStow:
                        SewingCycles++;
                        SetAction(work, day && work.Cycles < 12 ? VillageResidentAction.SewingUnpack : VillageResidentAction.SewingExit); break;
                    default: work.Active = false; return true;
                }
            }
            return false;
        }

        private static void SetAction(Work work, VillageResidentAction action) { work.Action = action; work.Time = 0f; }
        private static float Duration(VillageResidentAction action) => action == VillageResidentAction.SewingUnpack ? 6f :
            action == VillageResidentAction.SewingStow ? 5f :
            action == VillageResidentAction.RepairWork || action == VillageResidentAction.SewingWork ? 4f : 3f;

        private VillageWorkroomFrame PlaceProps(VillageResidentAction action, float time, Vector3 root, Quaternion rotation)
        {
            VillageWorkroomFrame frame = VillageResidentPresentation.SampleWorkroomProps(action, time);
            if (action == VillageResidentAction.RepairTakeTool || action == VillageResidentAction.RepairWork || action == VillageResidentAction.RepairPutTool)
                Place(hammer, frame.Hammer, root, rotation);
            else
            {
                Place(mitten, frame.Mitten, root, rotation); Place(cloth, frame.Cloth, root, rotation); Place(box, frame.Box, root, rotation);
                lid.localRotation = lidClosed * Quaternion.Euler(frame.BoxLidDegrees, 0f, 0f);
                flap.localRotation = flapOpen * Quaternion.Euler(0f, 0f, frame.ClothFoldDegrees);
                Vector3 start = root + rotation * frame.ThreadStart, end = root + rotation * frame.ThreadEnd;
                Vector3 strand = end - start;
                thread.SetPositionAndRotation(start, strand.sqrMagnitude > .000001f ?
                    Quaternion.FromToRotation(Vector3.up, strand.normalized) : rotation);
                thread.localScale = new Vector3(1f, Mathf.Max(.001f, strand.magnitude), 1f);
            }
            return frame;
        }
        private static void Place(Transform item, Pose pose, Vector3 root, Quaternion rotation) =>
            item.SetPositionAndRotation(root + rotation * pose.position, rotation * pose.rotation);

        private bool CanHelp() => repair.Active && repair.Action == VillageResidentAction.RepairWork &&
            !ChairFixed && !helpReserved && Environment.IsInside;
        private void ReserveHelp()
        { helpReserved = true; helperHolding = false; assistedStrokeDone = false; assistedStrokeStarted = false; }
        private void StartAssistedStroke()
        {
            helperHolding = true; assistedStrokeDone = false;
            // Complete the stroke already in progress before the assisted one.
            // Its cadence fits the remaining visible held interval without a
            // discontinuity in the tool or a strike after the hands release.
            assistedSpeed = Mathf.Max(1f, 4f / Mathf.Max(1.7f, 6f - Mathf.Max(0f, 4f - repair.Time) - .3f));
            if (repair.Time >= 4f) BeginAssistedCycle();
        }
        private void BeginAssistedCycle()
        {
            assistedStrokeStarted = true;
            SetAction(repair, VillageResidentAction.RepairWork);
        }
        private void FinishHelp(bool complete)
        {
            if (!helpReserved) return;
            helpReserved = false; helperHolding = false;
            if (complete && assistedStrokeDone)
            {
                FixChair(); SetAction(repair, VillageResidentAction.RepairPutTool);
                village.Life.SayWorkroom(VillageResidentRole.RepairNeighbor, "village.life.repair.thanks");
            }
            else if (ChairFixed) SetAction(repair, VillageResidentAction.RepairPutTool);
            else if (repair.Action == VillageResidentAction.RepairWork && repair.Time >= 4f)
                SetAction(repair, VillageResidentAction.RepairWork);
            assistedStrokeDone = false; assistedStrokeStarted = false;
        }
        private void FixChair() { GameSessionState.VillageHousehold.TryCompleteChairRepair(); SetRepairProgress(1f); }
        private void SetRepairProgress(float value)
        {
            repairProgress = Mathf.Max(repairProgress, Mathf.Clamp01(value));
            rail.position = Vector3.Lerp(Plan.Anchor("RepairRailRest"), Plan.Anchor("RepairRailFixed"), repairProgress);
        }

        public bool CanTalk(VillageResidentRole role)
        {
            Work work = State(role);
            return work != null && work.Active && !helpReserved &&
                (work.Action == VillageResidentAction.RepairWork || work.Action == VillageResidentAction.Idle || work.Action == VillageResidentAction.SewingWork);
        }
        public bool CanSeeResident(VillageResidentPresentation actor)
        {
            if (village.Player.GameObject == null) return false;
            Vector3 from = village.Player.GameObject.transform.position + Vector3.up * 1.5f;
            Vector3 to = actor.Head.position;
            foreach (RaycastHit hit in Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(actor.transform) && !hit.transform.IsChildOf(village.Player.GameObject.transform)) return false;
            return true;
        }
        public string WorkLine(VillageResidentRole role) => role == VillageResidentRole.RepairNeighbor ?
            (ChairFixed ? "village.life.repair.finished" : "village.life.repair.indoor") : "village.life.sewing.indoor";

        private void OnDisable() { Help?.Cancel(); Door?.Cancel(); }
    }
}
