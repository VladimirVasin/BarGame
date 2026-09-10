using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPortCrew
    {
        private enum BreakPhase { Work, Outward, Rest, Returning }
        private sealed class BreakWalk
        {
            public readonly Vector3[] Route = new Vector3[6];
            public BreakPhase Phase;
            public float Distance;
            public float Length;
            public Quaternion Facing;
            public float Settle;
        }

        private const float BreakWalkSpeed = 1.65f;
        private readonly BreakWalk[] breaks = new BreakWalk[3];
        private readonly Transform[] restDocks = new Transform[3];
        private readonly CityPortWorkerGesture[] gestures = new CityPortWorkerGesture[5];
        private readonly int[] speechPartners = { -1, -1, -1, -1, -1 };
        private readonly bool[] speaking = new bool[5];
        private readonly bool[] greeting = new bool[5];
        private readonly bool[] previouslyVisible = new bool[5];
        private readonly bool[] returnRequested = new bool[5];
        private readonly float[] conversationYaw = new float[5];
        private readonly int[] salutationPartners = { -1, -1, -1, -1, -1 };
        private readonly Transform[,] contactArms = new Transform[5, 6];
        private readonly Quaternion[] contactPose = new Quaternion[6];
        private bool sampledLife;
        private bool lifeSeek;
        private double previousPortSeconds;
        private float lifeDelta;
        private VillageResidentPresentation conversationDriver;

        public bool UseManualClock { get; set; }
        public double LifeElapsedSeconds { get; private set; }
        public bool WasLifeSeek => lifeSeek;
        private static double SessionLifeSeconds =>
            (GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes) /
            GameTimeState.GameMinutesPerRealSecond;

        public VillageResidentPresentation GetWorker(int role) =>
            role == CityPortConversationCatalog.DriverRole ? conversationDriver : workers[role];
        public void RegisterConversationDriver(VillageResidentPresentation driver) => conversationDriver = driver;
        public CityPortWorkerGesture GetGesture(int role) => gestures[role];
        public Bounds RestCanopyBounds { get; private set; }
        public Vector3 RestPosition(int role) => restDocks[role - 2].position;
        public bool IsRoleReturning(int role) => role >= 2 && breaks[role - 2].Phase == BreakPhase.Returning;

        public bool IsRoleWorking(int role)
        {
            if (role == 0) return LastSnapshot.Stage == CityPortCycleStage.Approach ||
                                  LastSnapshot.Stage == CityPortCycleStage.Depart;
            if (role == 2 || role == 3) return LastSnapshot.Stage == CityPortCycleStage.Unload;
            return LastSnapshot.Stage >= CityPortCycleStage.Moor &&
                   LastSnapshot.Stage <= CityPortCycleStage.Unmoor;
        }

        public bool IsRoleResting(int role) => role >= 2
            ? breaks[role - 2].Phase == BreakPhase.Rest
            : !IsRoleWorking(role) && workers[role].CurrentAction != VillageResidentAction.Walk;

        public bool IsRoleAvailableForSpeech(int role)
        {
            if (!initialized || !isActiveAndEnabled || !workers[role].gameObject.activeInHierarchy || returnRequested[role]) return false;
            if ((role == 2 || role == 3) && LastSnapshot.Stage == CityPortCycleStage.Prepare &&
                LastSnapshot.SecondsInStage >= CityPortCycle.PrepareDurationSeconds - 3d) return false;
            var action = workers[role].CurrentAction;
            return action != VillageResidentAction.Walk && action != VillageResidentAction.CarryWalk &&
                   action != VillageResidentAction.StationStrap &&
                   (speechPartners[role] >= 0 || !gestures[role].MouthBusy);
        }

        // A short exchange at the store can continue while the docker pushes
        // his trolley. The pose overlay already preserves his heading/grip.
        public bool IsDockerAvailableForDriverSpeech()
        {
            const int role = CityPortConversationCatalog.DockerRole;
            return initialized && isActiveAndEnabled && workers[role].gameObject.activeInHierarchy &&
                !returnRequested[role] && workers[role].CurrentAction != VillageResidentAction.StationStrap &&
                (speechPartners[role] == CityPortConversationCatalog.DriverRole || !gestures[role].MouthBusy);
        }

        public void SetSpeech(int role, int partner, bool isSpeaking, bool isGreeting)
        {
            speechPartners[role] = partner;
            speaking[role] = isSpeaking;
            greeting[role] = isGreeting;
            if (isGreeting && partner >= 0) salutationPartners[role] = partner;
        }

        private void InitializeSocialLife()
        {
            // Use the existing canopy roof, not the old western meeting points.
            // Its roof is wider than the posts. Transform the imported mesh
            // bounds directly: the newly positioned dock has not necessarily
            // reached the physics broadphase during runtime composition.
            Transform canopy = Require(port.Dock, "COL_Awning");
            Bounds meshBounds = canopy.GetComponent<MeshFilter>().sharedMesh.bounds;
            var cover = new Bounds(canopy.TransformPoint(meshBounds.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                var signs = new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                cover.Encapsulate(canopy.TransformPoint(meshBounds.center + Vector3.Scale(meshBounds.extents, signs)));
            }
            RestCanopyBounds = cover;
            Vector2[] places = { new Vector2(.45f, .70f), new Vector2(.75f, .70f), new Vector2(.60f, .28f) };
            string[] anchors = { "ANCHOR_RestWest", "ANCHOR_RestEast", "ANCHOR_RestQuay" };
            for (int i = 0; i < 3; i++)
            {
                restDocks[i] = Require(port.Dock, anchors[i]);
                restDocks[i].position = new Vector3(
                    Mathf.Lerp(RestCanopyBounds.min.x, RestCanopyBounds.max.x, places[i].x),
                    port.Plan.QuayTopY,
                    Mathf.Lerp(RestCanopyBounds.min.z, RestCanopyBounds.max.z, places[i].y));
                breaks[i] = new BreakWalk();
            }
            string[] arms = { "upper_arm.R", "forearm.R", "hand.R", "upper_arm.L", "forearm.L", "hand.L" };
            for (int role = 0; role < workers.Length; role++)
            {
                gestures[role] = new CityPortWorkerGesture(workers[role], role);
                for (int joint = 0; joint < arms.Length; joint++)
                    contactArms[role, joint] = Require(workers[role].ModelRoot, arms[joint]);
            }
        }

        private void BeginLifeSample(double portSeconds, double lifeSeconds)
        {
            if (double.IsNaN(lifeSeconds) || double.IsInfinity(lifeSeconds) || lifeSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(lifeSeconds));
            double step = lifeSeconds - LifeElapsedSeconds;
            lifeSeek = !sampledLife || step < 0d || step > 2d ||
                Math.Abs(portSeconds - previousPortSeconds) > Math.Max(2d, step + .1d);
            lifeDelta = lifeSeek ? 0f : (float)step;
            LifeElapsedSeconds = lifeSeconds;
            previousPortSeconds = portSeconds;
            sampledLife = true;
            if (!lifeSeek) return;
            for (int i = 0; i < workers.Length; i++)
            {
                speechPartners[i] = -1;
                speaking[i] = greeting[i] = false;
                conversationYaw[i] = 0f;
                salutationPartners[i] = -1;
                gestures[i].Reset();
            }
        }

        private void ApplySocialLife()
        {
            for (int role = 2; role < workers.Length; role++)
                if (workers[role].gameObject.activeInHierarchy)
                    ApplyBreak(role, lifeSeek || !previouslyVisible[role]);

            for (int role = 0; role < workers.Length; role++)
            {
                var actor = workers[role];
                if (!actor.gameObject.activeInHierarchy)
                {
                    gestures[role].Reset();
                    conversationYaw[role] = 0f;
                    salutationPartners[role] = -1;
                    previouslyVisible[role] = false;
                    continue;
                }
                bool moving = actor.CurrentAction == VillageResidentAction.Walk ||
                              actor.CurrentAction == VillageResidentAction.CarryWalk;
                bool handsFree = !moving && actor.CurrentAction != VillageResidentAction.StationStrap &&
                    (role == 0 ? CaptainHandsWeight() < .001f :
                     role == 2 || role == 3 ? CraneHandsWeight() < .001f : !IsRoleWorking(role) ||
                     role == 1 && LastSnapshot.Stage == CityPortCycleStage.Unload);
                Vector3? target = null;
                int partner = speechPartners[role];
                int facingPartner = partner >= 0 ? partner :
                    gestures[role].IsWaving || gestures[role].HasPendingWave ? salutationPartners[role] : -1;
                if ((role == 2 || role == 3) && LastSnapshot.Stage == CityPortCycleStage.Prepare &&
                    LastSnapshot.SecondsInStage >= CityPortCycle.PrepareDurationSeconds - 3d) facingPartner = -1;
                var partnerActor = facingPartner >= 0 ? GetWorker(facingPartner) : null;
                if (partnerActor != null && partnerActor.gameObject.activeInHierarchy)
                    target = partnerActor.Head.position;
                // Resters can look across their small group even between lines.
                else if (IsRoleResting(role) && role >= 2 &&
                    (long)((LifeElapsedSeconds + role * 2.7d) / (6d + role * .3d)) % 3 != 0)
                    target = restDocks[(role - 1) % 3].position + Vector3.up * 1.5f;
                ApplyConversationFacing(role, handsFree, moving,
                    facingPartner >= 0 ? target : null);
                gestures[role].Apply(LifeElapsedSeconds, lifeDelta, handsFree,
                    IsRoleResting(role) && !returnRequested[role], speaking[role], greeting[role], target,
                    lifeSeek || !previouslyVisible[role], partner >= 0);
                previouslyVisible[role] = true;
            }
        }

        private void ApplyConversationFacing(int role, bool handsFree, bool moving, Vector3? target)
        {
            var actor = workers[role];
            bool handlingRope = actor.CurrentAction == VillageResidentAction.StationStrap;
            float desired = 0f;
            if (target.HasValue && !moving && !handlingRope)
            {
                Vector3 direction = Vector3.ProjectOnPlane(target.Value - actor.transform.position, actor.transform.up);
                if (direction.sqrMagnitude > .001f)
                    desired = Vector3.SignedAngle(actor.transform.forward, direction, actor.transform.up);
                // A planted operator can acknowledge a colleague with the
                // torso while keeping the panel/helm contacts. Free workers
                // turn their whole stance, including the listening partner.
                if (!handsFree) desired = Mathf.Clamp(desired, -38f, 38f);
            }
            conversationYaw[role] = Mathf.DeltaAngle(0f,
                Mathf.MoveTowardsAngle(conversationYaw[role], desired, lifeDelta * 115f));
            if (moving || handlingRope) { conversationYaw[role] = 0f; return; }
            Quaternion turn = Quaternion.AngleAxis(conversationYaw[role], actor.transform.up);
            if (handsFree) actor.transform.rotation = turn * actor.transform.rotation;
            else ApplyWorkingConversationTurn(role);
        }

        private void ApplyWorkingConversationTurn(int role)
        {
            var actor = workers[role];
            Quaternion spineRotation = spines[role].rotation;
            Quaternion rightHand = contactArms[role, 2].rotation, leftHand = contactArms[role, 5].rotation;
            for (int joint = 0; joint < 6; joint++) contactPose[joint] = contactArms[role, joint].localRotation;
            float angle = Mathf.Clamp(conversationYaw[role], -38f, 38f);
            bool TryTurn(float yaw)
            {
                spines[role].rotation = Quaternion.AngleAxis(yaw, actor.transform.up) * spineRotation;
                for (int joint = 0; joint < 6; joint++) contactArms[role, joint].localRotation = contactPose[joint];
                contactArms[role, 2].rotation = rightHand;
                contactArms[role, 5].rotation = leftHand;
                return RestoreWorkingContacts(role);
            }
            bool matched = TryTurn(angle);
            if (!matched)
            {
                // Find the reachable limit continuously. Halving the last
                // visible yaw made the torso repeatedly advance and snap back
                // whenever a moving lever reached the arm's reach limit.
                float blocked = angle, reachable = 0f;
                matched = TryTurn(reachable);
                if (matched)
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        float candidate = (reachable + blocked) * .5f;
                        if (TryTurn(candidate)) reachable = candidate;
                        else blocked = candidate;
                    }
                angle = reachable;
                matched = TryTurn(angle);
            }
            conversationYaw[role] = angle;
            if (role == 0) CaptainHandsMatch = matched;
            else if (role == 2 || role == 3) CraneHandsMatch &= matched;
            else if (role == 4) TrolleyHandsMatch = matched;
        }

        private bool RestoreWorkingContacts(int role)
        {
            var actor = workers[role];
            if (role == 0) return actor.ApplyHandContacts(helmRight.position, helmLeft.position, CaptainHandsWeight());
            if (role == 2 || role == 3) return actor.ApplyHandContacts(controlsRight[role - 2].position,
                controlsLeft[role - 2].position, CraneHandsWeight());
            if (role != 4 || LastSnapshot.Stage != CityPortCycleStage.Unload) return true;
            Vector3 across = port.Trolley.right * .34f;
            return actor.ApplyHandContacts(trolleyHandle.position + across, trolleyHandle.position - across,
                EaseWindow((float)LastSnapshot.SecondsInStage, (float)CityPortCycle.UnloadDurationSeconds, .8f));
        }

        private double SecondsUntilRequired(int role)
        {
            var phase = LastSnapshot.Stage;
            double t = LastSnapshot.SecondsInStage;
            if (role == 4)
            {
                if (phase >= CityPortCycleStage.Moor && phase <= CityPortCycleStage.Unmoor) return 0d;
                if (phase == CityPortCycleStage.Approach) return CityPortCycle.ApproachDurationSeconds - t;
                if (phase == CityPortCycleStage.Depart)
                    return CityPortCycle.DepartDurationSeconds - t + CityPortCycle.IdleDurationSeconds +
                           CityPortCycle.ApproachDurationSeconds;
                return CityPortCycle.IdleDurationSeconds - t + CityPortCycle.ApproachDurationSeconds;
            }
            // A crane operator also watches the shared lifting area between
            // its own lifts. Excursions happen in the long vessel-free gaps,
            // so neither return route crosses a moving cargo/trolley corridor.
            if (phase == CityPortCycleStage.Unload) return 0d;
            if (phase == CityPortCycleStage.Approach) return CityPortCycle.UnloadStartSeconds - t;
            if (phase == CityPortCycleStage.Moor)
                return CityPortCycle.MoorDurationSeconds - t + CityPortCycle.PrepareDurationSeconds;
            if (phase == CityPortCycleStage.Prepare) return CityPortCycle.PrepareDurationSeconds - t;
            return CityPortCycle.CycleDurationSeconds -
                (previousPortSeconds % CityPortCycle.CycleDurationSeconds) + CityPortCycle.UnloadStartSeconds;
        }

        private void BuildBreakRoute(int role, BreakWalk walk)
        {
            Vector3 dock = role == 4 ? ShoreCleatDock(0) : operatorDocks[role - 2].position;
            Vector3 rest = restDocks[role - 2].position;
            float lane = port.Plan.Origin.z - 9.9f - (role - 2) * .65f;
            walk.Route[0] = dock;
            // The east worker passes east of the finite tare stack; the other
            // two pass behind the lifting area. Separate lanes stay north of
            // the warehouse and enter the canopy between its southern posts.
            walk.Route[1] = role == 3 ? new Vector3(port.Plan.Origin.x + 8.7f, dock.y, dock.z) : dock;
            walk.Route[2] = new Vector3(walk.Route[1].x, dock.y, lane);
            walk.Route[3] = new Vector3(rest.x, dock.y, lane);
            walk.Route[4] = new Vector3(rest.x, dock.y, RestCanopyBounds.min.z + .65f);
            walk.Route[5] = rest;
            walk.Length = 0f;
            for (int i = 1; i < walk.Route.Length; i++)
                walk.Length += Vector3.Distance(walk.Route[i - 1], walk.Route[i]);
        }

        private void ApplyBreak(int role, bool reconstruct)
        {
            var actor = workers[role];
            var walk = breaks[role - 2];
            BuildBreakRoute(role, walk);
            double until = SecondsUntilRequired(role);
            float journey = walk.Length / BreakWalkSpeed + 1.5f;
            if (reconstruct)
            {
                walk.Phase = until > journey * 2f + 8f ? BreakPhase.Rest : BreakPhase.Work;
                walk.Distance = walk.Phase == BreakPhase.Rest ? walk.Length : 0f;
                walk.Facing = actor.transform.rotation;
                walk.Settle = 0f;
                returnRequested[role] = false;
            }
            if (walk.Phase == BreakPhase.Work)
            {
                if (until <= journey * 2f + 8f) return;
                if (role < 4 && CraneHandsWeight() > .001f) return;
                if (role < 4 && LastSnapshot.Stage >= CityPortCycleStage.Secure && LastSnapshot.Stage <= CityPortCycleStage.Depart)
                    actor.Apply(VillageResidentAction.Idle, (float)(LifeElapsedSeconds % 600d) + role * .73f);
                // Finish the visit on the quay. Farewells start only once the
                // ship actually departs, before anyone sets off for a break.
                if (LastSnapshot.Stage == CityPortCycleStage.Secure || LastSnapshot.Stage == CityPortCycleStage.Unmoor ||
                    LastSnapshot.Stage == CityPortCycleStage.Depart &&
                    LastSnapshot.SecondsInStage < CityPortConversationSchedule.DepartureWindowSeconds)
                    return;
                if (speechPartners[role] >= 0 || gestures[role].IsWaving || Mathf.Abs(conversationYaw[role]) > .3f) return;
                walk.Phase = BreakPhase.Outward;
                walk.Facing = actor.transform.rotation;
                walk.Distance = 0f;
                walk.Settle = .8f;
            }
            if ((walk.Phase == BreakPhase.Outward || walk.Phase == BreakPhase.Rest) &&
                until <= journey + 3f + CityPortWorkerGesture.SmokeDurationSeconds)
            {
                returnRequested[role] = true;
                speechPartners[role] = -1;
                speaking[role] = greeting[role] = false;
                // Finish lowering the smoking/waving hand before walking.
                // The deadline reserves a complete puff as well as the route.
                if (!gestures[role].IsSmoking && !gestures[role].IsWaving && Mathf.Abs(conversationYaw[role]) < .3f)
                {
                    walk.Phase = BreakPhase.Returning;
                    walk.Settle = .8f;
                }
            }

            if (walk.Phase == BreakPhase.Rest)
            {
                Vector3 center = (restDocks[0].position + restDocks[1].position + restDocks[2].position) / 3f;
                Vector3 face = center - walk.Route[5];
                walk.Facing = Quaternion.RotateTowards(walk.Facing,
                    Quaternion.LookRotation(face, Vector3.up), lifeDelta * 100f);
                actor.transform.SetPositionAndRotation(walk.Route[5], walk.Facing);
                actor.Apply(VillageResidentAction.Idle, (float)(LifeElapsedSeconds % 600d) + role * .73f);
                return;
            }

            bool returning = walk.Phase == BreakPhase.Returning;
            float sign = returning ? -1f : 1f;
            float before = walk.Distance;
            walk.Settle = Mathf.Max(0f, walk.Settle - lifeDelta);
            float motion = Mathf.Clamp01(1f - walk.Settle / .8f);
            walk.Distance = Mathf.Clamp(before + sign * BreakWalkSpeed * lifeDelta * motion, 0f, walk.Length);
            Vector3 position = Along(walk.Route, walk.Distance);
            Vector3 direction = Along(walk.Route, Mathf.Clamp(walk.Distance + sign * .3f, 0f, walk.Length)) -
                                Along(walk.Route, Mathf.Clamp(walk.Distance - sign * .3f, 0f, walk.Length));
            if (returning && walk.Distance <= .001f) direction = Vector3.forward;
            if (direction.sqrMagnitude > .0001f)
                walk.Facing = Quaternion.RotateTowards(walk.Facing,
                    Quaternion.LookRotation(direction, Vector3.up), lifeDelta * 160f);
            actor.transform.SetPositionAndRotation(position, walk.Facing);
            actor.ApplyLocomotion(BreakWalkSpeed * motion, false, (float)(LifeElapsedSeconds % 600d));
            if (returning && walk.Distance <= .001f)
            {
                Quaternion facing = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                walk.Facing = Quaternion.RotateTowards(walk.Facing, facing, lifeDelta * 160f);
                actor.transform.rotation = walk.Facing;
                actor.Apply(VillageResidentAction.Idle, (float)(LifeElapsedSeconds % 600d));
                if (Quaternion.Angle(walk.Facing, facing) < .2f)
                {
                    walk.Phase = BreakPhase.Work;
                    returnRequested[role] = false;
                }
            }
            else if (!returning && walk.Distance >= walk.Length - .001f)
                walk.Phase = BreakPhase.Rest;
        }

        private float CraneHandsWeight()
        {
            if (LastSnapshot.Stage == CityPortCycleStage.Unload) return 1f;
            if (LastSnapshot.Stage == CityPortCycleStage.Prepare)
                return Smooth(((float)LastSnapshot.SecondsInStage -
                    (float)CityPortCycle.PrepareDurationSeconds + 1.5f) / 1.5f);
            if (LastSnapshot.Stage == CityPortCycleStage.Secure)
                return 1f - Smooth((float)LastSnapshot.SecondsInStage / 1.5f);
            return 0f;
        }

        private float CaptainHandsWeight()
        {
            if (LastSnapshot.Stage == CityPortCycleStage.Approach || LastSnapshot.Stage == CityPortCycleStage.Depart) return 1f;
            if (LastSnapshot.Stage == CityPortCycleStage.Moor)
                return 1f - Smooth((float)LastSnapshot.SecondsInStage / 1.5f);
            if (LastSnapshot.Stage == CityPortCycleStage.Unmoor)
                return Smooth(((float)LastSnapshot.SecondsInStage - (float)CityPortCycle.UnmoorDurationSeconds + 1.5f) / 1.5f);
            return 0f;
        }

        private void OnDisable()
        {
            for (int i = 0; i < gestures.Length; i++)
            {
                gestures[i]?.Reset();
                previouslyVisible[i] = false;
                returnRequested[i] = false;
                speechPartners[i] = -1;
                speaking[i] = greeting[i] = false;
                conversationYaw[i] = 0f;
                salutationPartners[i] = -1;
            }
            sampledLife = false;
        }
    }
}
