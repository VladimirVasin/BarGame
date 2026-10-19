using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CombatCranePhase { Waiting, Signal, Descending, Striking, Recovering, Ascending, Released, Frenzied,
        Capturing, ReturningHome, Watching, Regenerating }

    public sealed partial class CombatActor
    {
        private static readonly MeleeCombatSettings CraneCombatSettings = new MeleeCombatSettings(
            damage: 28f, blockCost: 24f, windupSeconds: .01f, activeSeconds: 1.4f,
            recoverySeconds: .55f, hitRecoverySeconds: .55f, blockRecoverySeconds: .55f,
            obstacleRecoverySeconds: .55f, parriedRecoverySeconds: .55f,
            animationRecoverySeconds: .55f, isUnarmedZombie: true, incomingDamageScale: .5f);
        private static readonly string[] CraneAnatomyBoneNames = {
            "pelvis", "spine", "chest", "neck", "head", "clavicle.L", "clavicle.R",
            "upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R", "hand.L", "hand.R",
            "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R"
        };
        private const float CraneSignalSeconds = .65f, CraneStrikeSeconds = 1.1f;
        public const float CraneMaximumAttackHeightAboveTarget = 2f;
        // The enlarged head keeps clear of the lower gantry cassette upstairs.
        private const float CraneRestHeight = .40f, CraneWellHalfWidth = 5f;
        internal const int CraneCableSegments = 4;
        private const float CraneFloorDwellSeconds = 2.5f, CraneStartSeconds = .6f, CraneBrakeSeconds = .7f;
        private readonly AnimationClip[] craneClips = new AnimationClip[10];
        private readonly Transform[] craneAttachments = new Transform[6], craneFeeds = new Transform[6], craneCables = new Transform[6];
        private readonly Transform[,] craneCablePieces = new Transform[6, CraneCableSegments];
        private readonly Vector3[,] craneCableScales = new Vector3[6, CraneCableSegments];
        private readonly float[] craneRopeSlack = new float[6], craneTetherLengths = new float[6];
        private readonly Vector3[] craneWaypoints = new Vector3[4];
        private RaycastHit[] craneVisibilityHits = new RaycastHit[16];
        private Transform craneHand, craneHookCentre, craneLeftHookCentre;
        private GameObject craneSuspensionHost, craneSuspensionHead, craneRails, craneBridge;
        private CombatCraneRemnant craneRemnant;
        private Transform craneFeedSwivel;
        private Quaternion craneFeedSwivelRest;
        private AudioSource craneWinch, craneRelease, craneRailVoice, craneBodyVoice;
        private bool craneWinchPaused, craneContactValid, craneStrikeStopped;
        private float craneClock, cranePerceptionWait, craneCooldown, craneHurtHold, craneStoppedPoseSeconds, craneBreathWait;
        private float craneMoveSpeed, craneMotorClock, craneBrakeClock, craneBlockedClock, craneHurtClock;
        private Vector3 craneDirection = Vector3.forward, craneSway, craneSwayVelocity;
        private Vector3 craneStationPosition, craneCommand, craneCommandVelocity;
        private int craneFloor = 2, craneDestinationFloor, cranePatrolStop, craneWaypointCount, craneWaypointIndex;
        private bool craneChangingFloors, craneTravelling, craneStarting;
        public bool IsCrane { get; private set; }
        public CombatCranePhase CranePhase { get; private set; } = CombatCranePhase.Released;
        public float CranePhaseElapsed => craneClock;
        public Vector3 CraneGroundPosition { get; private set; }
        public Vector3 CraneAnchorPosition { get; private set; }
        public Vector3 CraneCarriagePosition => craneCommand;
        public Vector3 CraneTravelGoal { get; private set; }
        public int CraneMotionAxis { get; private set; }
        public bool CraneTravelBlocked { get; private set; }
        public int CraneArrivedStops { get; private set; }
        public bool CraneAttached => IsCrane && CranePhase != CombatCranePhase.Released;
        internal bool HasWarehouseCraneRemnant => craneRemnant != null && craneRemnant.IsVisible;
        internal bool WarehouseCraneRemnantAnimating => craneRemnant != null && craneRemnant.IsAnimating;
        public int CraneFloorIndex => craneFloor;
        public bool CranePatrolTravelling => craneChangingFloors;
        internal bool CranePatrolEnabled { get; set; } = true;
        private float CraneFloorHeight => CraneGroundPosition.y + craneFloor * WarehouseArenaPlan.LevelRise;
        private Vector3 CraneStationPosition => craneStationPosition;
        public int CraneCableCount => IsCrane ? craneCables.Length : 0;
        public Vector3 CraneStrikeHandPosition => CraneHookPosition;
        public Vector3 GetCraneCableStart(int index) => craneFeeds[index].position;
        public Vector3 GetCraneCableEnd(int index) => HasWarehouseCraneRemnant ? craneRemnant.GetCableEnd(index) : craneAttachments[index].position;
        internal Transform GetCraneCableTransform(int index) => craneCables[index];
        internal Transform GetCraneCableTransform(int index, int segment) => craneCablePieces[index, segment];
        public Vector3 GetCraneCableSegmentStart(int index, int segment) => HasWarehouseCraneRemnant
            ? craneRemnant.GetPoint(index, segment / (float)CraneCableSegments) : CraneRopePoint(index, segment / (float)CraneCableSegments);
        public Vector3 GetCraneCableSegmentEnd(int index, int segment) => HasWarehouseCraneRemnant
            ? craneRemnant.GetPoint(index, (segment + 1f) / CraneCableSegments) : CraneRopePoint(index, (segment + 1f) / CraneCableSegments);

        private void LoadCraneClips()
        {
            for (int i = 0; i < craneClips.Length; i++) craneClips[i] = CombatCraneAssetProvider.LoadClip(CombatCraneAssetProvider.ClipNames[i]);
            ready = rest = defeat = craneClips[0]; hit = guardImpact = guardBreak = craneClips[9];
            zombieFrenzy = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.FrenzyClip);
        }

        private bool IsCraneClip(AnimationClip clip)
        {
            for (int i = 0; i < craneClips.Length; i++) if (craneClips[i] == clip) return true;
            return false;
        }

        private void InitializeCraneSuspension()
        {
            handPose = npc.RightGrip.GetComponentInParent<NpcHandPose>();
            craneHand = RequireCraneBone("hand.R");
            foreach (NpcHandPose.HandBinding binding in handPose.Hands)
                if (binding.IsLeft) craneLeftHookCentre = binding.CentreAnchor;
                else craneHookCentre = binding.CentreAnchor;
            craneSuspensionHost = new GameObject("Crane suspension");
            craneSuspensionHost.transform.SetParent(transform.parent, false);
            craneRails = CombatCraneAssetProvider.CreateRails(craneSuspensionHost.transform);
            craneBridge = CombatCraneAssetProvider.CreateBridge(craneSuspensionHost.transform);
            craneSuspensionHead = CombatCraneAssetProvider.CreateSuspensionHead(craneSuspensionHost.transform);
            craneFeedSwivel = CityPedestrianHandProps.FindSocket(craneSuspensionHead.transform, CombatCraneAssetProvider.HeadSwivelAnchor);
            if (craneFeedSwivel == null) throw new InvalidOperationException("Missing crane cable swivel.");
            craneFeedSwivelRest = craneFeedSwivel.rotation;
            for (int i = 0; i < craneCables.Length; i++)
            {
                craneAttachments[i] = RequireCraneBone(CombatCraneAssetProvider.SuspensionBoneNames[i]);
                Vector3 offset = CombatCraneAssetProvider.SuspensionLocalOffsets[i];
                if (offset.sqrMagnitude > 0f)
                {
                    Transform bone = craneAttachments[i];
                    var socket = new GameObject("Crane crown cable attachment");
                    socket.transform.SetParent(bone, false);
                    // Provider offsets are metres in the bone's orientation,
                    // independent of the retained imported rig's scale.
                    socket.transform.localPosition = bone.InverseTransformPoint(bone.position + bone.rotation * offset);
                    craneAttachments[i] = socket.transform;
                }
                craneFeeds[i] = CityPedestrianHandProps.FindSocket(craneSuspensionHead.transform, CombatCraneAssetProvider.HeadAnchorNames[i]);
                if (craneFeeds[i] == null) throw new InvalidOperationException("Missing authored crane cable feed.");
                // Siblings under a unit host retain the imported FBX scale100.
                for (int segment = 0; segment < CraneCableSegments; segment++)
                {
                    Transform piece = CombatCraneAssetProvider.CreateCableSegment(craneSuspensionHost.transform).transform;
                    craneCablePieces[i, segment] = piece; craneCableScales[i, segment] = piece.localScale;
                }
                craneCables[i] = craneCablePieces[i, 0];
            }
            craneRemnant = craneSuspensionHost.AddComponent<CombatCraneRemnant>();
            craneRemnant.Initialize(craneCablePieces, craneCableScales, GetComponentInParent<CombatTestRoot>());
            InitializeCraneClearance(); InitializeCraneDynamics(); InitializeCraneHook();
            craneWinch = CreateCraneVoice(craneSuspensionHead, true, CombatCraneAssetProvider.LoadWinchSound(), .26f);
            craneRailVoice = CreateCraneVoice(craneSuspensionHead, true, CombatCraneAssetProvider.LoadRailSound(), .24f);
            craneRelease = CreateCraneVoice(craneSuspensionHead, false, CombatCraneAssetProvider.LoadBrakeSound(), .38f);
            craneBodyVoice = CreateCraneVoice(gameObject, false, CombatCraneAssetProvider.LoadBodySound(), .20f);
            InitializeCraneFrenzy();
        }

        private Transform RequireCraneBone(string name) => CityPedestrianHandProps.FindSocket(DamageRigRoot, name)
            ?? throw new InvalidOperationException("Suspended worker requires " + name);

        private AudioSource CreateCraneVoice(GameObject host, bool loop, AudioClip clip, float volume)
        {
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = loop; source.clip = clip;
            source.spatialBlend = 1f; source.dopplerLevel = 0f; source.minDistance = 2f; source.maxDistance = 22f;
            source.rolloffMode = AudioRolloffMode.Linear; source.volume = volume;
            GameAudioMixer.Route(source, GameAudioGroup.SfxWorld);
            return source;
        }

        internal void ConfigureCraneSuspension(Vector3 ground, Vector3 ceiling)
        {
            if (HasWarehouseCraneRemnant) return;
            CraneGroundPosition = ground; CraneAnchorPosition = ceiling;
            CraneHomePosition = ground + Vector3.up * (WarehouseArenaPlan.TopHeight + CraneRestHeight);
            craneRails.transform.position = new Vector3(0f, ceiling.y, 0f);
            ResetCraneSuspension(); Present();
        }

        private void ResetCraneSuspension()
        {
            if (!IsCrane || craneSuspensionHead == null || HasWarehouseCraneRemnant) return;
            ResetCraneAttackLifecycle();
            ResetZombieFrenzy(); CraneFrenzyPlayCount = 0; CraneFrenzySecondsElapsed = 0f;
            craneClock = cranePerceptionWait = craneHurtHold = craneHurtClock = craneBreathWait = 0f; craneCooldown = .6f;
            craneFloor = craneDestinationFloor = 2; cranePatrolStop = CraneArrivedStops = 0;
            craneChangingFloors = craneTravelling = craneStarting = CraneTravelBlocked = false;
            CraneMotionAxis = 0; craneMoveSpeed = craneBrakeClock = craneMotorClock = 0f;
            craneSway = craneSwayVelocity = craneCommandVelocity = Vector3.zero;
            craneDirection = Vector3.forward;
            craneStationPosition = new Vector3(CraneGroundPosition.x, CraneFloorHeight + CraneRestHeight, CraneGroundPosition.z);
            craneCommand = CraneTravelGoal = craneStationPosition;
            craneContactValid = craneStrikeStopped = false; CranePhase = CombatCranePhase.Waiting;
            craneSafePoseReady = false;
            craneSuspensionHead.SetActive(true); craneSuspensionHost.SetActive(true);
            SetCraneRopesVisible(true); ResetCraneDynamics(); ResetCraneHook();
            StopCraneVoices(); craneWinchPaused = false;
            Body.enabled = false;
            transform.SetPositionAndRotation(craneCommand, Quaternion.identity); UpdateCraneMachinePose();
        }

        // The patrol owns its storey. A hero can invite an attack only at an
        // achieved stop, never choose the next patrol destination.
        internal bool TryBeginCraneCycle(CombatActor target)
        {
            if (RoomAttackSuppressed || !CraneAttached || craneTravelling || CranePhase != CombatCranePhase.Waiting || craneCooldown > 0f ||
                cranePerceptionWait > 0f || roundEnded || State.Phase != MeleePhase.Ready || target == null || target.State.IsDefeated) return false;
            cranePerceptionWait = .12f;
            if (!TryResolveCraneThrowDirection(target, out Vector3 direction)) return false;
            craneDirection = direction;
            SetCranePhase(CombatCranePhase.Signal); return true;
        }

        private bool TryResolveCraneThrowDirection(CombatActor target, out Vector3 direction)
        {
            direction = Vector3.zero;
            // Compare the two live bodies. The gantry remains at the ceiling,
            // and wrist animation must not change the permitted elevation.
            if (target == null || target.State.IsDefeated || target.Ragdoll == null ||
                !target.Ragdoll.PhysicsController.TryGetCombatBone(Player3DAnatomicalPart.Torso, out Transform targetChest) ||
                craneClearanceBones[2].position.y - targetChest.position.y > CraneMaximumAttackHeightAboveTarget) return false;
            Vector3 point = CraneTargetPoint(target), delta = point - CraneHookCableExit;
            if (delta.sqrMagnitude > CraneHookReach * CraneHookReach || delta.sqrMagnitude < .16f ||
                !CranePathVisible(CraneHookCableExit, point)) return false;
            direction = delta.normalized;
            return true;
        }

        private bool CranePathVisible(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .000001f) return true;
            RaycastHit[] hits = CombatTraversalQueries.Raycast(from, delta.normalized, ref craneVisibilityHits,
                delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++) if (hits[i].collider != null && IsContactWorld(hits[i].collider)) return false;
            return true;
        }

        private void SetCranePhase(CombatCranePhase phase)
        {
            CranePhase = phase; craneClock = 0f; BeginPoseBlend(.12f);
            if (phase == CombatCranePhase.Striking)
            {
                State.CancelAction();
                if (!State.RequestAttack()) { SetCranePhase(CombatCranePhase.Recovering); return; }
                craneContactValid = craneStrikeStopped = false; LaunchCraneHook();
            }
            if (phase == CombatCranePhase.Waiting) craneCooldown = .65f;
            JournalEvent("crane_phase", f0: GameLog.Field("phase", (int)phase));
        }

        private void AdvanceCraneSimulation(float seconds)
        {
            collectSweep = collectShove = collectKick = false; simulationPosePending = false;
            if (!CraneAttached || presentationFrozen || !RoomAttackSuppressed && !GameInput.CanRead(GameInputContext.Gameplay)) return;
            poseClock += seconds;
            craneCooldown = Mathf.Max(0f, craneCooldown - seconds); cranePerceptionWait = Mathf.Max(0f, cranePerceptionWait - seconds);
            if (poseBlendRemaining > 0f) poseBlendRemaining = Mathf.Max(0f, poseBlendRemaining - seconds);
            State.Advance(seconds, false);
            craneHurtClock = Mathf.Max(0f, craneHurtClock - seconds);
            craneHurtHold = Mathf.Max(0f, craneHurtHold - seconds);
            if (RoomAttackSuppressed)
            {
                AdvanceCraneRoomIdle(seconds);
                return;
            }
            if (!CranePatrolEnabled || roundEnded || contactTarget != null && contactTarget.State.IsDefeated)
            { ReleaseCraneVictim(false); StopCraneVoices(); return; }
            if (IsCraneFrenzied)
            {
                AdvanceCraneFrenzy(seconds);
                return;
            }
            craneBreathWait -= seconds;
            if (craneBreathWait <= 0f && !craneBodyVoice.isPlaying)
            { craneBodyVoice.clip = CombatCraneAssetProvider.LoadBodySound(); craneBodyVoice.Play(); craneBreathWait = 2f; }
            craneClock += seconds;
            if (craneRetreatRequested && CranePhase == CombatCranePhase.Waiting) BeginCraneReturnHome();
            Vector3 previous = transform.position, oldCommand = craneCommand;
            if (craneTravelling && craneHurtHold <= 0f)
            {
                if (AdvanceCraneTravel(seconds))
                {
                    craneFloor = craneDestinationFloor; craneStationPosition = CraneTravelGoal;
                    craneChangingFloors = false; CraneArrivedStops++;
                    if (CranePhase != CombatCranePhase.ReturningHome) SetCranePhase(CombatCranePhase.Waiting);
                }
            }
            else if (!craneTravelling) switch (CranePhase)
            {
                case CombatCranePhase.Waiting:
                    if (craneClock >= CraneFloorDwellSeconds) BeginCraneFloorTravel();
                    break;
                case CombatCranePhase.Signal:
                    if (contactTarget != null)
                    {
                        Vector3 aim = CraneTargetPoint(contactTarget) - CraneHookCableExit;
                        if (aim.sqrMagnitude > .001f) craneDirection = aim.normalized;
                    }
                    if (craneClock >= CraneSignalSeconds)
                    {
                        if (TryResolveCraneThrowDirection(contactTarget, out Vector3 releaseDirection))
                        {
                            craneDirection = releaseDirection;
                            SetCranePhase(CombatCranePhase.Striking);
                        }
                        else SetCranePhase(CombatCranePhase.Waiting);
                    }
                    break;
                case CombatCranePhase.Striking:
                    AdvanceCraneHook(seconds);
                    if (CraneHookPhase == CombatCraneHookPhase.Holding && craneHookHold >= .16f)
                    { BeginCraneHookReturn(); SetCranePhase(CombatCranePhase.Recovering); }
                    break;
                case CombatCranePhase.Recovering:
                    AdvanceCraneHook(seconds);
                    if (CraneHookPhase == CombatCraneHookPhase.Stowed && craneClock >= .6f)
                    { State.CancelAction(); SetCranePhase(CombatCranePhase.Waiting); }
                    break;
                case CombatCranePhase.Capturing:
                    AdvanceCraneCapture(seconds);
                    break;
                case CombatCranePhase.Watching:
                case CombatCranePhase.Regenerating:
                    AdvanceCraneWatch();
                    break;
            }
            if (CranePhase == CombatCranePhase.ReturningHome) AdvanceCraneHomeArrival(seconds);
            AdvanceCraneDynamics(seconds, (craneCommand - oldCommand) / Mathf.Max(.0001f, seconds));
            Vector3 direction = craneChangingFloors ? -Vector3.ProjectOnPlane(craneCommand, Vector3.up) : Vector3.ProjectOnPlane(craneDirection, Vector3.up);
            Quaternion rotation = direction.sqrMagnitude > .01f ? Quaternion.LookRotation(direction) : transform.rotation;
            float turnSpeed = CranePhase == CombatCranePhase.Signal ? 320f : 200f;
            if (!MoveCraneRoot(CraneLoadPosition, Quaternion.RotateTowards(transform.rotation, rotation, seconds * turnSpeed), craneCommand))
            { craneSwayVelocity *= -.15f; craneSway = Vector3.ProjectOnPlane(transform.position - craneCommand, Vector3.up); }
            UpdateCraneMachinePose();
            locomotionVelocity = (transform.position - previous) / Mathf.Max(.0001f, seconds);
        }

        private void SuppressCraneForRoomAttack()
        {
            if (!CraneAttached) return;
            ReleaseCraneVictim(false);
            craneTravelling = craneChangingFloors = craneStarting = false;
            craneWaypointCount = craneWaypointIndex = 0;
            craneMoveSpeed = craneBrakeClock = craneMotorClock = craneBlockedClock = 0f;
            CraneMotionAxis = 0; CraneTravelBlocked = false;
            craneStationPosition = CraneTravelGoal = craneCommand;
            craneContactValid = false; craneStrikeStopped = true;
            StopCraneMotors(); craneRelease.Stop();
            // The cinematic temporarily owns hostile movement. A pending
            // retreat resumes from this achieved position when that owner ends.
            if (CranePhase is not (CombatCranePhase.Watching or CombatCranePhase.Regenerating or CombatCranePhase.Frenzied))
                SetCranePhase(CombatCranePhase.Waiting);
            if (CraneHookPhase is CombatCraneHookPhase.Outbound or CombatCraneHookPhase.Holding)
                BeginCraneHookReturn();
        }

        private void AdvanceCraneRoomIdle(float seconds)
        {
            craneClock += seconds;
            AdvanceCraneHook(seconds);
            craneContactValid = collectSweep = sweepValid = false;
            // The carriage stays at its achieved position; residual pendulum
            // motion and the collision-aware hook return remain physical.
            AdvanceCraneDynamics(seconds, Vector3.zero);
            Vector3 previous = transform.position;
            if (!MoveCraneRoot(CraneLoadPosition, transform.rotation, craneCommand))
            {
                craneSwayVelocity *= -.15f;
                craneSway = Vector3.ProjectOnPlane(transform.position - craneCommand, Vector3.up);
            }
            UpdateCraneMachinePose();
            locomotionVelocity = (transform.position - previous) / Mathf.Max(.0001f, seconds);
        }

        private void BeginCraneFloorTravel()
        {
            // A deterministic circuit visits both sides and both ends of the
            // open hall, with floor decisions completely independent of hero Y.
            int stop = cranePatrolStop++ % 5;
            craneDestinationFloor = stop switch { 0 => 1, 1 => 0, 2 => 1, _ => 2 };
            // Leave the stretched limbs and their pendulum envelope inside
            // the open well while the body turns through a full attack circle.
            float x = stop switch { 0 or 3 => 3.8f, 1 or 2 => -3.8f, _ => 0f };
            float z = stop switch { 0 or 1 => 3.8f, 2 or 3 => -3.8f, _ => CraneGroundPosition.z };
            craneChangingFloors = true;
            SetCranePhase(craneDestinationFloor > craneFloor ? CombatCranePhase.Ascending : CombatCranePhase.Descending);
            BeginCraneTravel(new Vector3(x, CraneGroundPosition.y + craneDestinationFloor * WarehouseArenaPlan.LevelRise + CraneRestHeight, z));
        }

        private void BeginCraneTravel(Vector3 goal)
        {
            goal.x = Mathf.Clamp(goal.x, -CraneWellHalfWidth, CraneWellHalfWidth); goal.z = Mathf.Clamp(goal.z, -CraneWellHalfWidth, CraneWellHalfWidth);
            CraneTravelGoal = goal; craneWaypointCount = craneWaypointIndex = 0;
            Vector3 cursor = craneCommand;
            bool horizontal = Mathf.Abs(goal.x - cursor.x) + Mathf.Abs(goal.z - cursor.z) > .002f;
            // Above all seed-dependent ground racks/cargo before crossing them.
            float transit = horizontal ? Mathf.Max(3f, Mathf.Max(cursor.y, goal.y)) : cursor.y;
            AddCraneWaypoint(ref cursor, new Vector3(cursor.x, transit, cursor.z));
            AddCraneWaypoint(ref cursor, new Vector3(goal.x, cursor.y, cursor.z));
            AddCraneWaypoint(ref cursor, new Vector3(cursor.x, cursor.y, goal.z));
            AddCraneWaypoint(ref cursor, goal);
            craneTravelling = true; craneStarting = true; craneMotorClock = craneBrakeClock = craneMoveSpeed = craneBlockedClock = 0f;
            CraneTravelBlocked = false; BeginPoseBlend(.12f);
            craneRelease.clip = CombatCraneAssetProvider.LoadStartSound(); craneRelease.pitch = 1f; craneRelease.Play();
        }

        private void AddCraneWaypoint(ref Vector3 cursor, Vector3 point)
        {
            if (cursor.Equals(point)) return;
            craneWaypoints[craneWaypointCount++] = point; cursor = point;
        }

        private bool AdvanceCraneTravel(float seconds)
        {
            craneMotorClock += seconds;
            if (craneStarting)
            {
                if (craneMotorClock < CraneStartSeconds) return false;
                craneStarting = false; craneMotorClock = 0f;
            }
            if (craneBrakeClock > 0f)
            {
                craneBrakeClock = Mathf.Max(0f, craneBrakeClock - seconds);
                if (craneBrakeClock > 0f) return false;
            }
            if (craneWaypointIndex >= craneWaypointCount)
            { craneTravelling = false; CraneMotionAxis = 0; StopCraneMotors(); return true; }
            Vector3 waypoint = craneWaypoints[craneWaypointIndex], delta = waypoint - craneCommand;
            int axis = delta.y != 0f ? 2 : delta.x != 0f ? 1 : 3;
            if (CraneMotionAxis != axis) { BeginPoseBlend(.12f); craneMotorClock = 0f; }
            CraneMotionAxis = axis;
            float distance = delta.magnitude;
            craneMoveSpeed = Mathf.MoveTowards(craneMoveSpeed, Mathf.Min(axis == 2 ? 2.4f : 3.4f, Mathf.Sqrt(10f * distance)), seconds * 5f);
            Vector3 candidate = Vector3.MoveTowards(craneCommand, waypoint, craneMoveSpeed * seconds);
            // Command and head only advance when the complete suspended load can
            // actually follow. A stopwatch never declares an obstructed arrival.
            bool moved = MoveCraneRoot(candidate + CraneLoadOffset, transform.rotation, candidate);
            CraneTravelBlocked = !moved;
            craneBlockedClock = moved ? 0f : craneBlockedClock + seconds;
            if (!moved) { craneMoveSpeed = 0f; StopCraneMotors(); return false; }
            craneCommand = candidate;
            UpdateCraneMotorSound(axis, delta.y);
            if (craneCommand.Equals(waypoint))
            {
                craneWaypointIndex++; craneMoveSpeed = 0f; craneBrakeClock = CraneBrakeSeconds;
                StopCraneMotors();
                craneRelease.clip = CombatCraneAssetProvider.LoadBrakeSound(); craneRelease.pitch = .85f; craneRelease.Play(); BeginPoseBlend(.1f);
            }
            return false;
        }

        private void UpdateCraneMachinePose()
        {
            CraneAnchorPosition = new Vector3(craneCommand.x, CraneAnchorPosition.y, craneCommand.z);
            craneSuspensionHead.transform.position = CraneAnchorPosition;
            craneFeedSwivel.rotation = Quaternion.AngleAxis(transform.eulerAngles.y, Vector3.up) * craneFeedSwivelRest;
            craneBridge.transform.position = new Vector3(0f, CraneAnchorPosition.y, craneCommand.z);
        }

        private void UpdateCraneMotorSound(int axis, float vertical)
        {
            if (axis == 2)
            {
                craneRailVoice.Stop(); craneWinch.pitch = vertical > 0f ? .86f : .67f;
                craneWinch.volume = Mathf.Lerp(.12f, .28f, craneMoveSpeed / 2.4f);
                if (!craneWinch.isPlaying) craneWinch.Play();
            }
            else
            {
                craneWinch.Stop(); craneRailVoice.pitch = Mathf.Lerp(.68f, 1.04f, craneMoveSpeed / 3.4f);
                if (!craneRailVoice.isPlaying) craneRailVoice.Play();
            }
        }

        private void StopCraneMotors() { craneWinch.Stop(); craneRailVoice.Stop(); }
        private void StopCraneVoices() { StopCraneMotors(); craneRelease.Stop(); craneBodyVoice.Stop(); }

        private bool MoveCraneRoot(Vector3 wanted, Quaternion rotation, Vector3 carriage)
        {
            wanted.x = Mathf.Clamp(wanted.x, -5.2f, 5.2f); wanted.z = Mathf.Clamp(wanted.z, -5.2f, 5.2f);
            if (!CraneSuspensionClear(wanted, rotation, carriage)) return false;
            transform.SetPositionAndRotation(wanted, rotation); return true;
        }

        private void PresentCrane()
        {
            if (!CraneAttached) return;
            int index = CranePhase switch { CombatCranePhase.Signal => 1, CombatCranePhase.Descending => 2,
                CombatCranePhase.Striking => 3, CombatCranePhase.Capturing or CombatCranePhase.Recovering => 4,
                CombatCranePhase.Ascending or CombatCranePhase.ReturningHome => 5, _ => 0 };
            if (craneTravelling) index = craneStarting ? 7 : craneBrakeClock > 0f ? 8 : CraneMotionAxis is 1 or 3 ? 6 : index;
            if (craneHurtClock > 0f) index = 9;
            AnimationClip clip = craneClips[index];
            string clipName = CombatCraneAssetProvider.ClipNames[index];
            float clock = index == 0 ? poseClock : index == 9 ? .55f - craneHurtClock : index == 8 ? CraneBrakeSeconds - craneBrakeClock : index >= 6 ? craneMotorClock : craneClock;
            float time = CombatCraneAssetProvider.IsLoop(clipName) ? Mathf.Repeat(clock, clip.length) : Mathf.Min(clock, clip.length);
            if (CranePhase == CombatCranePhase.Striking && craneStrikeStopped) time = craneStoppedPoseSeconds;
            // Every presentation starts with the authored pose; secondary
            // dynamics are persistent state, never accumulated bone writes.
            for (int i = 0; i < npcAnimationBaseBones.Length; i++)
            {
                npcAnimationBaseBones[i].SetLocalPositionAndRotation(npcAnimationBasePositions[i], npcAnimationBaseRotations[i]);
                npcAnimationBaseBones[i].localScale = npcAnimationBaseScales[i];
            }
            if (IsCraneFrenzied) PresentCraneFrenzy();
            else SampleNpcClip(clip, time);
            handPose?.SetGrip(false, CombatCraneAssetProvider.RightHookGrip); handPose?.SetGrip(true, CombatCraneAssetProvider.LeftHookGrip);
            visibleClip = IsCraneFrenzied ? CombatZombieAssetProvider.FrenzyClip : clipName;
            ApplyNpcPoseBlend();
            ApplyCraneDynamicPose();
            KeepCranePoseClear(); RefreshCraneAttachmentSockets();
            PresentCraneHook(); RememberNpcPresentedPose(); PresentCraneRopes();
        }

        private void ReceiveCraneImpulse(CombatImpact impact)
        {
            if (impact.Result == MeleeHitResult.Ignored || IsCraneRegenerating || HasWarehouseCraneRemnant) return;
            craneSwayVelocity += Vector3.ClampMagnitude(Vector3.ProjectOnPlane(impact.Impulse, Vector3.up) * .008f, .65f);
            craneHurtHold = .18f; craneHurtClock = .55f;
            BeginPoseBlend(.08f);
            craneBodyVoice.clip = CombatCraneAssetProvider.LoadHurtSound(); craneBodyVoice.Play(); craneBreathWait = 1.2f;
            if (CranePhase == CombatCranePhase.Striking) StopCraneStrike();
        }

        private void PlayCraneContactSound(MeleeHitResult result)
        {
            if (!IsCrane || result == MeleeHitResult.Ignored || HasWarehouseCraneRemnant) return;
            craneBodyVoice.PlayOneShot(CombatCraneAssetProvider.LoadReleaseSound(), .9f);
        }

        private void StopCraneStrike()
        {
            if (!craneStrikeStopped) craneStoppedPoseSeconds = Mathf.Min(craneClock, CraneStrikeSeconds);
            craneStrikeStopped = true; collectSweep = false;
            State.CancelAttackOnObstacle(); StopCraneHookFlight();
        }

        private void ReleaseCraneSuspension()
        {
            if (!CraneAttached) return;
            ReleaseCraneVictim(false);
            CranePhase = CombatCranePhase.Released;
            craneChangingFloors = craneTravelling = false;
            CraneMotionAxis = 0;
            craneContactValid = collectSweep = false;
            StopCraneVoices();
            craneRelease.clip = CombatCraneAssetProvider.LoadReleaseSound(); craneRelease.pitch = .72f; craneRelease.Play();
            SetCraneRopesVisible(false); ReleaseCraneHook();
        }

        private void SuspendCrane()
        {
            if (!IsCrane || craneSuspensionHead == null) return;
            ReleaseCraneVictim(false);
            EndZombieFrenzy();
            StopCraneVoices();
            if (!HasWarehouseCraneRemnant) craneSuspensionHost.SetActive(false);
        }

        /// <summary>The encounter removes the body separately; this keeps the achieved gantry and six free cables.</summary>
        internal void SetWarehouseCraneRemnant(bool visible, bool animate = false)
        {
            if (!IsCrane || craneRemnant == null || visible == HasWarehouseCraneRemnant) return;
            if (!visible)
            {
                craneRemnant.Clear();
                ResetCraneSuspension();
                if (isActiveAndEnabled) PresentCrane();
                else craneSuspensionHost.SetActive(false);
                return;
            }
            ResetCraneAttackLifecycle();
            EndZombieFrenzy();
            State.CancelAction();
            CranePhase = CombatCranePhase.Released;
            craneChangingFloors = craneTravelling = craneStarting = false;
            CraneMotionAxis = 0;
            craneMoveSpeed = 0f;
            craneContactValid = collectSweep = sweepValid = collectShove = collectKick = false;
            StopCraneVoices();
            CombatCorpseTraversal.ReleaseCollider(craneHookCollider);
            CombatCorpseTraversal.ReleaseCollider(craneCuffCollider);
            craneHookBody.isKinematic = craneCuffBody.isKinematic = true;
            craneHookBody.useGravity = craneCuffBody.useGravity = false;
            craneHookCollider.enabled = craneCuffCollider.enabled = false;
            craneHookFrame.SetActive(false);
            craneCuffFrame.SetActive(false);
            craneAttackCable.gameObject.SetActive(false);
            CraneHookPhase = CombatCraneHookPhase.Released;
            craneSuspensionHost.SetActive(true);
            craneSuspensionHead.SetActive(true);
            craneRemnant.Show(craneFeeds, craneAttachments, animate);
        }

        private void SetCraneAudioFrozen(bool frozen)
        {
            if (!IsCrane || craneWinch == null) return;
            if (frozen && !craneWinchPaused) { craneWinch.Pause(); craneRelease.Pause(); craneRailVoice.Pause(); craneBodyVoice.Pause(); }
            else if (!frozen && craneWinchPaused) { craneWinch.UnPause(); craneRelease.UnPause(); craneRailVoice.UnPause(); craneBodyVoice.UnPause(); }
            craneWinchPaused = frozen; FreezeCraneHook(frozen);
        }

        private void DisposeCrane()
        {
            ReleaseCraneVictim(false);
            if (craneRemnant != null) craneRemnant.Clear();
            CombatCorpseTraversal.ReleaseCollider(craneHookCollider); CombatCorpseTraversal.ReleaseCollider(craneCuffCollider);
            if (craneSuspensionHost != null) Destroy(craneSuspensionHost);
        }
    }
}
