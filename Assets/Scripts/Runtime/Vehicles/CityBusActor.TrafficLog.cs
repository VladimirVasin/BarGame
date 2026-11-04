using UnityEngine;

namespace BarPromenade
{
    internal enum CityBusBlockerKind
    {
        Unknown, BusBody, BusPrediction, Player, PlayerPrediction,
        Pedestrian, PedestrianPrediction, CanneryTruck, CanneryReservation
    }

    public readonly struct CityBusObstacleState
    {
        public static CityBusObstacleState Clear => new CityBusObstacleState(false, float.PositiveInfinity);

        public CityBusObstacleState(bool mustStop, float forwardClearance)
            : this(mustStop, forwardClearance, CityBusBlockerKind.Unknown, default) { }

        internal CityBusObstacleState(bool mustStop, float forwardClearance, CityBusBlockerKind kind,
            Vector3 point, string id = null, int index = -1, CityBusActor bus = null)
        {
            MustStop = mustStop;
            ForwardClearance = mustStop && !float.IsNaN(forwardClearance) && !float.IsInfinity(forwardClearance)
                ? Mathf.Max(0f, forwardClearance) : float.PositiveInfinity;
            Kind = kind;
            Point = point;
            Id = id;
            Index = index;
            BusState = bus?.MotionState;
            BusStallId = bus?.TrafficStallId ?? 0;
            BusStopIndex = bus?.CurrentStopIndex ?? -1;
        }

        public bool MustStop { get; }
        public float ForwardClearance { get; }
        internal CityBusBlockerKind Kind { get; }
        internal Vector3 Point { get; }
        internal string Id { get; }
        internal int Index { get; }
        internal CityBusMotionState? BusState { get; }
        internal long BusStallId { get; }
        internal int BusStopIndex { get; }

        internal bool SameSource(CityBusObstacleState other) =>
            Kind == other.Kind && Index == other.Index && Id == other.Id;
    }

    public sealed partial class CityBusActor
    {
        // Issued on the main thread when an incident first becomes observable. Shared across
        // vehicles and City rebuilds so equal timestamps in a single session are unambiguous.
        private static long nextStallId;
        internal long TrafficStallId { get; private set; }
        internal int FleetVehicleIndex { get; set; }
        private float approachStallElapsed;
        private bool approachStallReported;
        private int stallStartedFrame;
        private CityBusObstacleState lastBlocker;
        private int approachStopIndex = -1;
        private float approachStopDistance = -1f;

        private void ReportApproachStall(float travelled, float requestedTravel, float deltaTime,
            bool approachingStop, int stopIndex, float stopDistance, CityBusObstacleState obstacles)
        {
            bool yielded = MotionState == CityBusMotionState.Yielding;
            if ((!yielded && MotionState != CityBusMotionState.ApproachingStop) || travelled > 0f)
            {
                EndStall(travelled > 0f ? "moved" : "no_longer_waiting", travelled, deltaTime);
                return;
            }

            if (approachStallElapsed == 0f) stallStartedFrame = Time.frameCount;
            approachStallElapsed += deltaTime;
            approachStopIndex = approachingStop ? stopIndex : -1;
            approachStopDistance = approachingStop ? stopDistance - distanceAlongLink : -1f;

            // Sub-centimetre approach steps can legitimately bank travel for the next frame.
            // Log an immediate onset only when obstacle braking actually forbids moving.
            bool hardStop = yielded && obstacles.MustStop && speed == 0f &&
                obstacles.ForwardClearance <= ObstacleStopPadding;
            if (hardStop && TrafficStallId == 0)
            {
                TrafficStallId = ++nextStallId;
                stallStartedFrame = Time.frameCount;
                WriteTrafficEvent("yield_started", obstacles, deltaTime, requestedTravel);
            }
            else if (hardStop && TrafficStallId != 0 && !lastBlocker.SameSource(obstacles))
            {
                WriteTrafficEvent("yield_blocker_changed", obstacles, deltaTime, requestedTravel);
            }

            if (hardStop || TrafficStallId == 0) lastBlocker = obstacles;
            if (approachStallReported || approachStallElapsed < ApproachStallReportSeconds) return;

            approachStallReported = true;
            if (TrafficStallId == 0) TrafficStallId = ++nextStallId;
            WriteTrafficEvent("approach_stalled", obstacles, deltaTime, requestedTravel);
        }

        private void EndStall(string reason, float travelled = 0f, float deltaTime = 0f)
        {
            if (approachStallElapsed == 0f) return;
            if (TrafficStallId != 0)
                WriteTrafficEvent("approach_released", lastBlocker, deltaTime, travelled: travelled, reason: reason);
            TrafficStallId = 0;
            approachStallElapsed = 0f;
            approachStallReported = false;
            lastBlocker = default;
        }

        private void WriteTrafficEvent(string eventName, CityBusObstacleState blocker,
            float deltaTime, float requestedTravel = 0f, float travelled = 0f, string reason = null)
        {
            if (GameLog.Profile == GameLogProfile.Off) return;
            Vector3 pos = Position;
            CityBusStopDescriptor closest = null;
            float distanceSq = float.PositiveInfinity;
            foreach (var stop in plan.Stops)
            {
                float sq = (pos - stop.Position).sqrMagnitude;
                if (sq >= distanceSq) continue;
                closest = stop;
                distanceSq = sq;
            }
            string stopCode = closest == null ? null :
                new CityBusStopLabel(plan.Number, closest.SequenceIndex + 1).Code;
            bool identified = blocker.Kind != CityBusBlockerKind.Unknown;

            GameLog.Write(eventName == "approach_stalled" ? GameLogLevel.Warning : GameLogLevel.Info,
                "bus", eventName, new[] {
                    GameLog.Field("route_id", plan.RouteId), GameLog.Field("route_number", plan.Number),
                    GameLog.Field("vehicle_index", FleetVehicleIndex),
                    GameLog.Field("bus_on_line", FleetVehicleIndex % CityBusDirector.VehiclesPerLine + 1),
                    GameLog.Field("stall_id", TrafficStallId), GameLog.Field("started_frame", stallStartedFrame),
                    GameLog.Field("x", pos.x), GameLog.Field("y", pos.y), GameLog.Field("z", pos.z),
                    GameLog.Field("link_id", CurrentLink.Id), GameLog.Field("along_link_m", distanceAlongLink),
                    GameLog.Field("nearest_stop", stopCode),
                    GameLog.Field("nearest_stop_m", closest == null ? -1f : Mathf.Sqrt(distanceSq)),
                    GameLog.Field("background", !IsSpawned), GameLog.Field("state", MotionState.ToString()),
                    GameLog.Field("blocker_kind", BlockerName(blocker)),
                    GameLog.Field("blocker_id", blocker.Id), GameLog.Field("blocker_index", identified ? blocker.Index : -1),
                    GameLog.Field("blocker_bus_state", blocker.BusState?.ToString()),
                    GameLog.Field("blocker_stall_id", blocker.BusStallId), GameLog.Field("blocker_stop_index", blocker.BusStopIndex),
                    PointField("blocker_x", blocker.Point.x), PointField("blocker_y", blocker.Point.y), PointField("blocker_z", blocker.Point.z),
                    GameLog.Field("stop_index", approachStopIndex), GameLog.Field("distance_to_stop", approachStopDistance),
                    GameLog.Field("speed", speed), GameLog.Field("must_stop", blocker.MustStop),
                    GameLog.Field("forward_clearance", blocker.MustStop ? blocker.ForwardClearance : -1f),
                    GameLog.Field("delta_time", deltaTime), GameLog.Field("requested_travel", requestedTravel),
                    GameLog.Field("pending_travel", pendingTravel), GameLog.Field("travelled", travelled),
                    GameLog.Field("stalled_s", approachStallElapsed), GameLog.Field("reason", reason)
                });

            GameLogField PointField(string key, float value) =>
                identified ? GameLog.Field(key, value) : GameLog.Field(key, (string)null);
        }

        private static string BlockerName(CityBusObstacleState obstacle) => obstacle.Kind switch
        {
            CityBusBlockerKind.BusBody => "bus_body",
            CityBusBlockerKind.BusPrediction => "bus_prediction",
            CityBusBlockerKind.Player => "player",
            CityBusBlockerKind.PlayerPrediction => "player_prediction",
            CityBusBlockerKind.Pedestrian => "pedestrian",
            CityBusBlockerKind.PedestrianPrediction => "pedestrian_prediction",
            CityBusBlockerKind.CanneryTruck => "cannery_truck",
            CityBusBlockerKind.CanneryReservation => "cannery_reservation",
            _ => obstacle.MustStop ? "unknown" : "none"
        };
    }
}
