using System;
using UnityEngine;

namespace BarPromenade
{
    // The two neighbours share a vestibule, not a timetable. A meeting is only
    // claimed when the returning body is actually approaching the outgoing one.
    [DefaultExecutionOrder(230)]
    internal sealed class CityCourtDoorCourtesy : MonoBehaviour
    {
        internal static readonly Vector2Int Address = new(-40, -21);
        private const float Inside = -1.42f, HoldDepth = -.49f, Lane = .30f;
        private const float HingeAcross = .615f, HingeDepth = -.16f, LeafScale = 1.25f;
        private readonly Resident[] residents = new Resident[2];
        private CityHillsideBuilding house;
        private Camera observer;
        private CharacterController hero;
        private Transform hinge, shoulder, elbow, hand;
        private float yard, openTime, heldSeconds, braceWeight;
        private double lastTick, nextCamera, nextPose;
        private bool releasedAnimation;
        internal float OpenFraction => Mathf.SmoothStep(0f, 1f, openTime);
        internal bool IsHolding { get; private set; }
        internal Transform DoorHinge => hinge;
        internal Transform HolderElbow => elbow;
        internal Transform ResidentBody(int index) => residents[index].actor.transform;

        private sealed class Resident
        {
            internal VillageResidentPresentation actor;
            internal float distance, speed, side;
            internal bool outward;
        }

        internal static CityCourtDoorCourtesy Build(Transform parent, CityHillsideBuilding house)
        {
            var host = new GameObject("Passage court vestibule").transform;
            host.SetParent(parent, false);
            host.SetPositionAndRotation(house.ThresholdWorld, house.Lot.FacadeRotation);
            var door = host.gameObject.AddComponent<CityCourtDoorCourtesy>();
            door.house = house;
            // End both trips on the forecourt, before the public footway.
            door.yard = Mathf.Min(3.15f, Vector3.Dot(house.StreetContactWorld - house.ThresholdWorld, host.forward) - .65f);
            var frame = new GameObject("Vestibule frame").transform;
            frame.SetParent(host, false);
            frame.localPosition = Vector3.forward * HingeDepth;
            frame.localScale = new Vector3(LeafScale, 1.06f, 1f);
            door.hinge = new GameObject("Vestibule hinge").transform;
            door.hinge.SetParent(host, false);
            door.hinge.localPosition = new Vector3(HingeAcross, .026f, HingeDepth);
            door.hinge.localScale = frame.localScale;
            foreach (var part in VillageResidentDoorAssets.Manifest.door_parts)
                VillageResidentDoorAssets.CreatePart(part, part.role == "Frame" ? frame : door.hinge, part.role != "Handle");
            for (int i = 0; i < door.residents.Length; i++)
            {
                var actor = DefaultNpcFactory.CreateForCharacter(host, i == 0
                    ? DefaultNpcPopulation.CourtDoorResident : DefaultNpcPopulation.CourtDoorNeighbour);
                CityPortCrew.AlignWorkerModelWithPlacement(actor);
                var body = actor.gameObject.AddComponent<CapsuleCollider>();
                body.radius = .21f; body.height = 1.68f; body.center = Vector3.up * .84f;
                door.residents[i] = new Resident { actor = actor, side = i == 0 ? Lane : -Lane };
            }
            Transform rig = door.residents[0].actor.ModelRoot;
            door.shoulder = CityPedestrianHandProps.FindSocket(rig, "upper_arm.R");
            door.elbow = CityPedestrianHandProps.FindSocket(rig, "forearm.R");
            door.hand = CityPedestrianHandProps.FindSocket(rig, "hand.R");
            door.Seek(Clock());
            return door;
        }

        private static double Clock() => (GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes)
            / GameTimeState.GameMinutesPerRealSecond;

        // Each resident leaves home, spends time in the forecourt and comes
        // back. Different periods also produce uneventful, solitary visits.
        private float ScheduledDistance(int i, double seconds)
        {
            double phase = (seconds + (i == 0 ? 0d : 33d)) % (i == 0 ? 145d : 129d);
            float outAt = i == 0 ? 3f : 0f, inAt = i == 0 ? 45f : 36f;
            float pace = i == 0 ? .84f : .98f;
            return phase < inAt ? Mathf.Clamp(Inside + ((float)phase - outAt) * pace, Inside, yard)
                : Mathf.Max(Inside, yard - ((float)phase - inAt) * pace);
        }

        internal void Seek(double seconds)
        {
            IsHolding = false;
            braceWeight = 0f;
            lastTick = nextPose = seconds;
            openTime = 0f;
            for (int i = 0; i < residents.Length; i++)
            {
                var r = residents[i];
                r.distance = ScheduledDistance(i, seconds);
                r.outward = ScheduledDistance(i, seconds + .02d) > r.distance;
                r.speed = 0f;
                if (r.distance > Inside + .01f && r.distance < 1.42f) openTime = 1f;
                Place(r);
            }
            SetLeaf();
            foreach (var r in residents) UpdatePresence(r, openTime > 0f);
        }

        private void OnEnable()
        {
            if (hinge) Seek(Clock());
        }

        private void OnDisable()
        {
            IsHolding = false;
            foreach (var r in residents) if (r != null && r.actor) r.actor.ReleaseAnimation();
            releasedAnimation = true;
        }

        private void OnDestroy()
        {
            foreach (var r in residents)
                if (r != null && r.actor) NpcFootstepSources.Unregister(r.actor.transform);
        }

        private void LateUpdate()
        {
            if (GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning || PauseMenuController.IsAnyPaused) return;
            if (!observer && Time.unscaledTimeAsDouble >= nextCamera)
            {
                observer = Camera.main;
                hero = observer ? observer.GetComponentInParent<CharacterController>() : null;
                nextCamera = Time.unscaledTimeAsDouble + 1d;
            }
            double now = Clock(), dt = now - lastTick;
            if (dt < 0d || dt > 1d) { Seek(now); return; }
            bool near = observer && (observer.transform.position - transform.position).sqrMagnitude < 40f * 40f;
            if (dt < (near ? 1d / 60d : .25d)) return;
            bool sample = near && now >= nextPose;
            if (sample) nextPose = now + 1d / 30d;
            if (!near && !releasedAnimation)
            {
                foreach (var r in residents) r.actor.ReleaseAnimation();
                releasedAnimation = true;
            }
            Advance(now, (float)dt, hero && hero.enabled ? hero : null, sample, near);
        }

        internal void Advance(double now, float dt, CharacterController player, bool samplePose, bool audible)
        {
            lastTick = now;
            Resident first = residents[0], other = residents[1];
            float a = ScheduledDistance(0, now), b = ScheduledDistance(1, now);
            bool personNear = false;
            if (player)
            {
                Vector3 p = transform.InverseTransformPoint(player.bounds.center);
                personNear = Mathf.Abs(p.x) < 1.4f && p.y > -.1f && p.y < 2.65f && p.z > -2f && p.z < 2f;
            }
            bool requestOpen = personNear || IsHolding || NeedsDoor(first, a) || NeedsDoor(other, b);
            foreach (var r in residents) UpdatePresence(r, requestOpen || openTime > 0f);
            float previous = openTime;
            openTime = Mathf.MoveTowards(openTime, requestOpen ? 1f : 0f, dt / (requestOpen ? .52f : 1.25f));
            SetLeaf();
            if (audible && previous > 0f && openTime == 0f)
                RetroAudio.PlayAt(RetroSfxId.BoothLatch, hinge.position, .19f);

            float before = first.distance;
            Move(other, b, dt, player);
            if (IsHolding)
            {
                heldSeconds += dt;
                if (other.distance <= HoldDepth - .65f || heldSeconds >= 4f || a < HoldDepth)
                    IsHolding = false;
            }
            Move(first, IsHolding ? HoldDepth : a, dt, player);
            if (!IsHolding && before < HoldDepth && first.distance >= HoldDepth && openTime >= .995f &&
                other.speed > .05f && !other.outward && other.distance > HoldDepth + .45f && other.distance < 2.35f)
            {
                IsHolding = true;
                heldSeconds = 0f;
                first.distance = HoldDepth;
                first.speed = 0f;
                Place(first);
            }
            if (samplePose)
            {
                releasedAnimation = false;
                foreach (var r in residents)
                    if (r.actor.gameObject.activeSelf) r.actor.ApplyLocomotion(r.speed, false, (float)(now % 1200d));
            }
            bool reaching = a > before && first.outward && first.distance < HoldDepth &&
                first.distance > HoldDepth - .25f && !other.outward && other.speed > .05f &&
                other.distance > HoldDepth + .45f && other.distance < 2.8f && openTime >= .995f;
            braceWeight = Mathf.MoveTowards(braceWeight, IsHolding || reaching ? 1f : 0f, dt / .19f);
            if (samplePose && braceWeight > 0f) BraceElbow();
        }

        private void SetLeaf() => hinge.localRotation = Quaternion.Euler(0f, 180f - 90f * OpenFraction, 0f);

        private static void UpdatePresence(Resident resident, bool openingOrOpen)
        {
            // Retire or restore only behind the shut, opaque leaf. A player
            // already in the vestibule keeps both the leaf and the bodies live.
            bool visible = resident.distance > Inside + .001f || openingOrOpen;
            if (resident.actor.gameObject.activeSelf != visible)
                resident.actor.gameObject.SetActive(visible);
        }

        private static bool NeedsDoor(Resident r, float target) =>
            r.distance > Inside + .04f && r.distance < 1.42f ||
            target > r.distance && r.distance <= HoldDepth || target < r.distance && r.distance < 1.75f;

        private void Move(Resident r, float target, float dt, CharacterController player)
        {
            float previous = r.distance;
            float next = Mathf.MoveTowards(previous, target, dt * 1.04f);
            if (openTime < .97f)
            {
                // The solid leaf completes its swing before a body enters its sweep.
                if (previous <= -1.26f) next = Mathf.Min(next, -1.26f);
                if (previous >= .52f) next = Mathf.Max(next, .52f);
            }
            if (player && next != previous)
            {
                Vector3 p = transform.InverseTransformPoint(player.bounds.center);
                float clearance = player.radius + .235f;
                if (p.y > -.1f && p.y < 2.6f && Mathf.Abs(r.side - p.x) < clearance &&
                    Mathf.Abs(next - p.z) < clearance && Mathf.Abs(next - p.z) < Mathf.Abs(previous - p.z)) next = previous;
            }
            r.distance = next;
            r.speed = Mathf.Abs(next - previous) / dt;
            if (r.speed > .01f) r.outward = next > previous;
            Place(r);
        }

        private void Place(Resident r)
        {
            var pos = transform.TransformPoint(new Vector3(r.side, 0f, r.distance));
            if (house.TrySampleApproach(new Vector2(pos.x, pos.z), out float floor)) pos.y = floor;
            var facing = r.outward ? transform.forward : -transform.forward;
            r.actor.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing));
        }

        private void BraceElbow()
        {
            // Intersection between the upper arm's reach sphere and the INSIDE
            // face of the fully parked leaf. Keep the wrist folded above it.
            Vector3 normal = transform.right;
            float toFace = Vector3.Dot(hinge.position - shoulder.position, normal) - .034f;
            float length = Vector3.Distance(shoulder.position, elbow.position);
            toFace = Mathf.Clamp(toFace, -length, length);
            Vector3 along = (-transform.up - .25f * transform.forward).normalized;
            Vector3 reach = normal * toFace + along * Mathf.Sqrt(Mathf.Max(0f, length * length - toFace * toFace));
            Quaternion upper = Quaternion.FromToRotation(elbow.position - shoulder.position, reach) * shoulder.rotation;
            shoulder.rotation = Quaternion.Slerp(shoulder.rotation, upper, braceWeight);
            Quaternion lower = Quaternion.FromToRotation(hand.position - elbow.position,
                transform.up + .45f * transform.forward) * elbow.rotation;
            elbow.rotation = Quaternion.Slerp(elbow.rotation, lower, braceWeight);
        }
    }
}
