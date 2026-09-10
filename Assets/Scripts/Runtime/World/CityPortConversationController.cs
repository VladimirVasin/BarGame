using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The port crew and visiting driver share one local, pause-aware bubble channel.</summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class CityPortConversationController : MonoBehaviour
    {
        public const float RestPairRangeMeters = 6f;
        public const float WorkingPairRangeMeters = 30f;
        private static readonly string[] voices =
        {
            NpcVoiceCatalog.FishermanDesignId, NpcVoiceCatalog.ChessPlayerDesignId,
            NpcVoiceCatalog.CheckersPlayerDesignId, NpcVoiceCatalog.CafeManDesignId,
            NpcVoiceCatalog.WatchmanDesignId, NpcVoiceCatalog.CafeManDesignId
        };
        private CityPortController port;
        private CityPortCrew crew;
        private Camera worldCamera, lastCamera;
        private Transform explicitListener, lastListener;
        private NpcSpeechBubbleView bubbles;
        private CityPortConversationSchedule schedule;
        private int shownSerial = -1;
        private bool hasVisibleLine;
        private VillageResidentPresentation driver;
        private CityPortWorkerGesture driverGesture;
        private bool driverPresent, driverAvailable, driverHandsFree, driverWorking;
        private bool driverGreetingWindow, driverFarewellWindow, driverSpeaking, driverGreeting, driverConversing;
        private double driverPoseSeconds = double.NaN;
        private float driverYaw;
        private int accessWaitingRole = -1, pendingAccessRole = -1, accessLineRole = -1;
        private double previousAccessSeconds = double.NaN, accessLineUntil;
        private long lastAccessToken = -1;
        public const string AccessWaitLineKey = "city.port.access_wait";
        public int AccessWaitLinesPlayed { get; private set; }

        public CityPortConversationSchedule Schedule => schedule;
        public NpcSpeechBubbleView Bubbles => bubbles;
        public string LastLineKey { get; private set; } = string.Empty;
        public int LastSpeakerRole { get; private set; } = -1;

        public void Initialize(CityPortController controller, CityPortCrew portCrew,
            Camera camera = null, Transform listener = null, int seed = 3197)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (portCrew == null) throw new ArgumentNullException(nameof(portCrew));
            ClearPresentation();
            if (crew != null && bubbles != null)
                for (int role = 0; role < CityPortConversationCatalog.RoleCount; role++)
                    bubbles.WithdrawSpeaker(crew.GetWorker(role));
            port = controller;
            crew = portCrew;
            crew.RegisterConversationDriver(driver);
            worldCamera = camera;
            explicitListener = listener;
            schedule = new CityPortConversationSchedule(seed);
            bubbles = GetComponent<NpcSpeechBubbleView>();
            if (bubbles == null) bubbles = gameObject.AddComponent<NpcSpeechBubbleView>();
            bubbles.UseManualClock = true;
            bubbles.LineDurationSeconds = (float)CityPortConversationSchedule.LineSeconds;
            bubbles.Initialize(camera, listener);
            for (int role = 0; role < CityPortConversationCatalog.RoleCount; role++)
            {
                var actor = crew.GetWorker(role);
                if (actor != null) bubbles.DeclareSpeaker(actor, actor.Head, voices[role], NpcEarshotProfile.Shout);
            }
            shownSerial = -1;
            accessWaitingRole = pendingAccessRole = accessLineRole = -1;
            previousAccessSeconds = double.NaN;
            lastAccessToken = -1;
            AccessWaitLinesPlayed = 0;
        }

        public void RegisterDriver(VillageResidentPresentation actor)
        {
            if (driver == actor) return;
            if (driver != null && bubbles != null) bubbles.WithdrawSpeaker(driver);
            driverGesture?.Reset();
            driver = actor;
            // Reuse the dock worker's small gesture vocabulary; the visiting
            // driver never enters its smoking/break branch.
            driverGesture = actor != null ? new CityPortWorkerGesture(actor, CityPortConversationCatalog.DockerRole) : null;
            crew?.RegisterConversationDriver(actor);
            driverPoseSeconds = double.NaN;
            driverYaw = 0f;
            if (actor != null && bubbles != null)
                bubbles.DeclareSpeaker(actor, actor.Head, voices[CityPortConversationCatalog.DriverRole], NpcEarshotProfile.Shout);
        }

        public void SetDriverState(bool present, bool available, bool handsFree, bool working,
            bool greetingWindow, bool farewellWindow)
        {
            driverPresent = present;
            driverAvailable = present && available;
            driverHandsFree = present && handsFree;
            driverWorking = present && working;
            driverGreetingWindow = present && greetingWindow;
            driverFarewellWindow = present && farewellWindow;
            if (present) return;
            driverSpeaking = driverGreeting = driverConversing = false;
            driverGesture?.Reset();
            driverYaw = 0f;
        }

        /// <summary>A physical queue arrival, never the walk towards it. The
        /// delivery token keeps a missed or reconstructed wait from repeating.</summary>
        public void SetStoreAccessWait(int waitingRole, double workingSeconds, long waitToken)
        {
            if (waitingRole != CityPortConversationCatalog.DriverRole && waitingRole != CityPortConversationCatalog.DockerRole)
                waitingRole = -1;
            double step = workingSeconds - previousAccessSeconds;
            bool continuous = !double.IsNaN(previousAccessSeconds) && step >= 0d && step <= .5d;
            long token = waitToken * 2 + (waitingRole == CityPortConversationCatalog.DockerRole ? 1 : 0);
            if (!continuous)
            {
                pendingAccessRole = accessLineRole = -1;
                if (waitingRole >= 0) lastAccessToken = Math.Max(lastAccessToken, token);
            }
            if (continuous && waitingRole >= 0 && waitingRole != accessWaitingRole && token > lastAccessToken)
            {
                pendingAccessRole = waitingRole;
                lastAccessToken = token;
            }
            accessWaitingRole = waitingRole;
            previousAccessSeconds = workingSeconds;
        }

        /// <summary>Apply immediately after the cannery samples its base driver pose.
        /// Walking keeps its heading; occupied hands retain their authored contacts.</summary>
        public void ApplyDriverSocialPose()
        {
            if (driver == null || driverGesture == null || crew == null || !driverPresent ||
                !driver.gameObject.activeInHierarchy) return;
            double life = crew.LifeElapsedSeconds, step = life - driverPoseSeconds;
            bool seek = double.IsNaN(driverPoseSeconds) || step < 0d || step > 2d;
            float dt = seek ? 0f : (float)step;
            driverPoseSeconds = life;
            if (seek) driverYaw = 0f;
            bool moving = driver.CurrentAction == VillageResidentAction.Walk ||
                driver.CurrentAction == VillageResidentAction.CarryWalk;
            bool turnBody = driverHandsFree && !moving;
            var docker = crew.ShoreWorker;
            bool looking = driverConversing || driverGesture.IsWaving || driverGesture.HasPendingWave;
            Vector3? target = looking && docker != null && docker.gameObject.activeInHierarchy
                ? docker.Head.position : (Vector3?)null;
            float desired = 0f;
            if (turnBody && target.HasValue)
            {
                Vector3 direction = Vector3.ProjectOnPlane(target.Value - driver.transform.position, Vector3.up);
                if (direction.sqrMagnitude > .001f)
                    desired = Vector3.SignedAngle(driver.transform.forward, direction, Vector3.up);
            }
            driverYaw = turnBody ? Mathf.MoveTowardsAngle(driverYaw, desired, dt * 115f) : 0f;
            if (turnBody) driver.transform.rotation = Quaternion.AngleAxis(driverYaw, Vector3.up) * driver.transform.rotation;
            driverGesture.Apply(life, dt, driverHandsFree, false, driverSpeaking, driverGreeting, target, seek, driverConversing);
        }

        private void LateUpdate()
        {
            if (crew == null || schedule == null || crew.UseManualClock) return;
            bool paused = !GameSessionState.IsGameTimeRunning || GameTimeScaleRuntime.IsPaused;
            bubbles.RenderEnabled = !paused;
            if (!paused) ApplyAt();
        }

        /// <summary>Consumes the crew's already-sampled life clock; also the manual capture path.</summary>
        public void ApplyAt()
        {
            if (port == null || crew == null || schedule == null) return;
            if (worldCamera == null) worldCamera = Camera.main;
            Transform listener = explicitListener != null ? explicitListener : port.PresentationObserver;
            if (listener == null && worldCamera != null) listener = worldCamera.transform;
            if (lastListener != listener || lastCamera != worldCamera)
            {
                lastListener = listener;
                lastCamera = worldCamera;
                bubbles.Initialize(worldCamera, listener);
            }
            int available = 0, working = 0, resting = 0, audible = 0;
            uint nearbyPairs = 0, closePairs = 0;
            for (int role = 0; role < CityPortConversationCatalog.RoleCount; role++)
            {
                var actor = crew.GetWorker(role);
                if (actor == null || !actor.gameObject.activeInHierarchy) continue;
                int bit = 1 << role;
                bool isDriver = role == CityPortConversationCatalog.DriverRole;
                if (isDriver && !driverPresent) continue;
                if (isDriver ? driverAvailable : crew.IsRoleAvailableForSpeech(role)) available |= bit;
                if (isDriver ? driverWorking : crew.IsRoleWorking(role)) working |= bit;
                if (isDriver ? driverAvailable && !driverWorking : crew.IsRoleResting(role)) resting |= bit;
                if (listener != null && Vector3.SqrMagnitude(actor.Head.position - listener.position) <=
                    NpcEarshotProfile.ShoutCullRadiusMeters * NpcEarshotProfile.ShoutCullRadiusMeters)
                    audible |= bit;
                for (int partner = 0; partner < role; partner++)
                {
                    var other = crew.GetWorker(partner);
                    if (other == null || !other.gameObject.activeInHierarchy) continue;
                    float distance = Vector3.SqrMagnitude(actor.Head.position - other.Head.position);
                    uint pair = CityPortConversationCatalog.PairBit(role, partner);
                    if (distance <= WorkingPairRangeMeters * WorkingPairRangeMeters) nearbyPairs |= pair;
                    float restEarshot = NpcEarshotProfile.ConversationFaintRadiusMeters;
                    if (distance <= RestPairRangeMeters * RestPairRangeMeters && listener != null &&
                        Vector3.SqrMagnitude(actor.Head.position - listener.position) <= restEarshot * restEarshot &&
                        Vector3.SqrMagnitude(other.Head.position - listener.position) <= restEarshot * restEarshot)
                        closePairs |= pair;
                }
            }
            // Both turns must remain in earshot. A reply cannot arrive from a
            // missing/cull-hidden partner, nor be replayed after walking back.
            available &= audible;
            bool accessLine = ApplyAccessWaitLine(audible);
            var turn = schedule.Advance(crew.LifeElapsedSeconds, port.ElapsedSeconds, port.Snapshot,
                available, working, resting, nearbyPairs, closePairs,
                !accessLine && listener != null && audible != 0 && (port.ShorePresentationActive || port.VesselPresentationActive),
                driverGreetingWindow && driverAvailable, driverFarewellWindow && driverAvailable,
                (audible & (1 << CityPortConversationCatalog.DockerRole)) != 0 && crew.IsDockerAvailableForDriverSpeech());
            for (int role = 0; role < crew.WorkerCount; role++)
                crew.SetSpeech(role, -1, false, false);
            driverSpeaking = driverGreeting = driverConversing = false;
            if (accessLine)
            {
                int partner = accessLineRole == CityPortConversationCatalog.DriverRole ?
                    CityPortConversationCatalog.DockerRole : CityPortConversationCatalog.DriverRole;
                SetSpeech(accessLineRole, partner, true, false);
                SetSpeech(partner, accessLineRole, false, false);
                bubbles.AdvanceTo((float)crew.LifeElapsedSeconds);
                return;
            }
            if (turn.HasExchange)
            {
                int first = turn.Exchange.FirstRole, second = turn.Exchange.SecondRole;
                bool salutation = turn.Exchange.Kind == CityPortConversationKind.Greeting ||
                    turn.Exchange.Kind == CityPortConversationKind.Farewell;
                SetSpeech(first, second, turn.IsSpeaking && turn.SpeakerRole == first,
                    salutation && turn.IsSpeaking && turn.SpeakerRole == first);
                SetSpeech(second, first, turn.IsSpeaking && turn.SpeakerRole == second,
                    salutation && turn.IsSpeaking && turn.SpeakerRole == second);
            }
            if (!turn.IsSpeaking)
            {
                if (hasVisibleLine) bubbles.DismissAll();
                hasVisibleLine = false;
            }
            else if (shownSerial != turn.LineSerial)
            {
                bubbles.DismissAll();
                var speaker = crew.GetWorker(turn.SpeakerRole);
                var earshot = turn.Exchange.Kind == CityPortConversationKind.Rest ?
                    NpcEarshotProfile.Conversation : NpcEarshotProfile.Shout;
                bubbles.DeclareSpeaker(speaker, speaker.Head, voices[turn.SpeakerRole], earshot);
                bubbles.LineDurationSeconds = (float)CityPortConversationSchedule.LineDuration(turn.Exchange);
                bubbles.ShowAt(speaker, LocalizationService.Get(turn.LineKey), (float)crew.LifeElapsedSeconds);
                shownSerial = turn.LineSerial;
                hasVisibleLine = true;
                LastLineKey = turn.LineKey;
                LastSpeakerRole = turn.SpeakerRole;
            }
            bubbles.AdvanceTo((float)crew.LifeElapsedSeconds);
        }

        private bool ApplyAccessWaitLine(int audible)
        {
            int pair = (1 << CityPortConversationCatalog.DriverRole) | (1 << CityPortConversationCatalog.DockerRole);
            bool heard = driverPresent && port.ShorePresentationActive && (audible & pair) == pair;
            double now = crew.LifeElapsedSeconds;
            if (!heard || now >= accessLineUntil) accessLineRole = -1;
            if (pendingAccessRole >= 0)
            {
                int role = pendingAccessRole;
                pendingAccessRole = -1;
                if (heard && role == accessWaitingRole)
                {
                    var actor = crew.GetWorker(role);
                    bubbles.DismissAll();
                    bubbles.DeclareSpeaker(actor, actor.Head, voices[role], NpcEarshotProfile.Shout);
                    bubbles.LineDurationSeconds = 2.5f;
                    bubbles.ShowAt(actor, LocalizationService.Get(AccessWaitLineKey), (float)now);
                    accessLineRole = role;
                    accessLineUntil = now + 2.5d;
                    hasVisibleLine = true;
                    LastLineKey = AccessWaitLineKey;
                    LastSpeakerRole = role;
                    AccessWaitLinesPlayed++;
                }
            }
            return accessLineRole >= 0;
        }

        private void SetSpeech(int role, int partner, bool speaking, bool salutation)
        {
            if (role != CityPortConversationCatalog.DriverRole)
            { crew.SetSpeech(role, partner, speaking, salutation); return; }
            driverConversing = partner == CityPortConversationCatalog.DockerRole;
            driverSpeaking = driverConversing && speaking;
            driverGreeting = driverConversing && salutation;
        }

        private void OnDisable()
        {
            schedule?.Reset();
            accessWaitingRole = pendingAccessRole = accessLineRole = -1;
            previousAccessSeconds = double.NaN;
            ClearPresentation();
        }

        private void ClearPresentation()
        {
            if (bubbles != null) bubbles.DismissAll();
            hasVisibleLine = false;
            driverSpeaking = driverGreeting = driverConversing = false;
            driverGesture?.Reset();
            driverPoseSeconds = double.NaN;
            driverYaw = 0f;
            if (crew == null) return;
            for (int role = 0; role < crew.WorkerCount; role++)
                crew.SetSpeech(role, -1, false, false);
        }
    }
}
