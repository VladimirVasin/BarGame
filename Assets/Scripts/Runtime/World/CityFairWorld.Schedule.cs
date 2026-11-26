using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityFairWorld
    {
        internal const int OpeningMinute = 9 * 60, ClosingMinute = 22 * 60;
        private sealed class Participant
        {
            internal Transform Root;
            internal VillageResidentPresentation Adult;
            internal CityFairChildPresentation Child;
            internal int ChildIndex;
            internal Vector3 Station;
            internal Quaternion Facing;
            internal CityWalkingPath Morning, Evening;
            internal float Speed, Release, Departure;
            internal bool Releasing;
        }

        private Participant[] participants;
        private CityFairHomeRoutes routes;
        private int[] homeBySlot, departureOrder;
        private int assignedNight = int.MinValue;
        private double lastMinute = double.NaN;
        private bool allInside;
        private float longestTripSeconds;
        public bool IsOpen { get; private set; }

        private void InitializeSchedule()
        {
            participants = new Participant[Vendors.ActorCount + Adults.ActorCount + Children.ActorCount];
            routes = new CityFairHomeRoutes(Plan, participants.Length);
            homeBySlot = new int[participants.Length];
            departureOrder = new int[participants.Length];
            int slot = 0;
            foreach (var vendor in Vendors.Actors) Add(vendor, null, -1);
            foreach (var adult in Adults.Actors) Add(adult, null, -1);
            for (int i = 0; i < Children.ActorCount; i++) Add(null, Children.Actors[i], i);
            RefreshSchedule();

            void Add(VillageResidentPresentation adult, CityFairChildPresentation child, int index)
            {
                Transform root = adult != null ? adult.transform : child.transform;
                var lane = routes.ToGate(slot, root.position);
                participants[slot] = new Participant
                {
                    Root = root, Adult = adult, Child = child, ChildIndex = index,
                    Station = root.position, Facing = root.rotation, Morning = lane, Evening = lane,
                    Speed = (child != null ? .80f : 1.12f) + .035f * (slot % 4)
                };
                slot++;
            }
        }

        private void Update()
        {
            if (participants != null && !GameTimeScaleRuntime.IsPaused) RefreshSchedule();
        }

        internal void RefreshSchedule()
        {
            int day = GameSessionState.GameDayIndex;
            double minute = GameSessionState.GameTimeOfDayMinutes;
            double now = day * 1440d + minute;
            if (lastMinute == now) return;
            bool seek = double.IsNaN(lastMinute) || Math.Abs(now - lastMinute) > 1d;
            lastMinute = now;
            if (minute >= OpeningMinute && minute < ClosingMinute)
            {
                if (!IsOpen) Open();
                return;
            }

            const int middleOfNight = (ClosingMinute + OpeningMinute - 1440) / 2;
            bool afterMidnight = minute < middleOfNight;
            bool evening = minute >= ClosingMinute || afterMidnight;
            int night = minute >= ClosingMinute ? day : day - 1;
            if (IsOpen || assignedNight != night)
            {
                bool liveClose = IsOpen && minute >= ClosingMinute;
                IsOpen = false;
                if (Organ != null) Organ.enabled = Bell.enabled = false;
                Vendors.AtFair = Adults.AtFair = Children.AtFair = false;
                Adults.ReleaseStances();
                routes.Assign(night, homeBySlot);
                assignedNight = night;
                longestTripSeconds = 0f;
                allInside = false;
                for (int i = 0; i < participants.Length; i++)
                {
                    Participant p = participants[i];
                    p.Release = 0f;
                    p.Releasing = false;
                    p.Evening = p.Morning;
                    if (liveClose && p.Child != null)
                    {
                        p.Release = Children.BeginDeparture(p.ChildIndex);
                        p.Releasing = true;
                        p.Evening = routes.ToGate(i, p.Root.position);
                    }
                    else if (p.Child != null) Children.LeaveActivity(p.ChildIndex);
                    departureOrder[i] = i;
                }
                // Reserve space where the two aisles meet. The same street speed
                // keeps that gap when a child and an adult take the same pavement.
                Array.Sort(departureOrder, (a, b) => EarliestGate(a).CompareTo(EarliestGate(b)));
                float lastGate = -2f;
                foreach (int i in departureOrder)
                {
                    Participant p = participants[i];
                    lastGate = Mathf.Max(lastGate + 1.25f, EarliestGate(i));
                    p.Departure = lastGate - p.Evening.Length / p.Speed;
                    longestTripSeconds = Mathf.Max(longestTripSeconds, Mathf.Max(lastGate, p.Morning.Length / p.Speed)
                        + routes.Home(homeBySlot[i]).Length / StreetSpeed);
                }
            }

            float seconds = (float)((evening ? minute + (afterMidnight ? 1440 : 0) - ClosingMinute : OpeningMinute - minute)
                / GameTimeState.GameMinutesPerRealSecond);
            if (allInside && seconds >= longestTripSeconds) return;
            allInside = true;
            for (int i = 0; i < participants.Length; i++)
            {
                Participant p = participants[i];
                if (evening && seconds < p.Release)
                {
                    Children.SampleDeparture(p.ChildIndex, p.Release - seconds);
                    allInside = false;
                    continue;
                }
                if (p.Releasing)
                {
                    Children.LeaveActivity(p.ChildIndex);
                    p.Releasing = false;
                }
                CityWalkingPath local = evening ? p.Evening : p.Morning;
                CityWalkingPath home = routes.Home(homeBySlot[i]);
                float tripSeconds = evening ? Mathf.Max(0f, seconds - p.Departure) : seconds;
                float onPavement = tripSeconds - local.Length / p.Speed;
                float distance = onPavement > 0f ? local.Length + onPavement * StreetSpeed : tripSeconds * p.Speed;
                if (distance >= local.Length + home.Length)
                {
                    p.Root.position = home.Points[home.Points.Count - 1];
                    SetPresent(p, false);
                    continue;
                }

                allInside = false;
                SetPresent(p, true);
                bool onStreet = distance >= local.Length;
                var path = onStreet ? home : local;
                p.Root.position = path.Sample(onStreet ? distance - local.Length : distance, out Vector3 direction);
                bool moving = !evening || seconds >= p.Departure;
                if (moving && direction.sqrMagnitude > .001f)
                {
                    Quaternion facing = Quaternion.LookRotation(evening ? direction : -direction);
                    p.Root.rotation = seek ? facing : Quaternion.RotateTowards(p.Root.rotation, facing, 240f * Time.deltaTime);
                }
                float stride = evening ? distance : local.Length + home.Length - distance;
                if (p.Child != null)
                {
                    p.Child.Sample(moving ? CityFairChildAction.StreetWalk : CityFairChildAction.Idle,
                        stride / CityFairChildActions.StreetCycleMetres * CityFairChildPresentation.Duration(CityFairChildAction.StreetWalk));
                }
                else p.Adult.ApplyLocomotion(moving ? (onStreet ? StreetSpeed : p.Speed) : 0f, false, stride);
            }
        }

        private const float StreetSpeed = 1.12f;

        private float EarliestGate(int slot)
        {
            Participant p = participants[slot];
            return p.Release + slot * .4f + p.Evening.Length / p.Speed;
        }

        private void Open()
        {
            IsOpen = true;
            Vendors.AtFair = Adults.AtFair = Children.AtFair = true;
            if (Organ != null) Organ.enabled = Bell.enabled = true;
            foreach (Participant p in participants)
            {
                SetPresent(p, true);
                p.Root.SetPositionAndRotation(p.Station, p.Facing);
            }
            Vendors.ApplyAt(0f);
            Adults.ApplyAt(0f);
            Children.Restart();
        }

        private static void SetPresent(Participant p, bool value)
        {
            if (p.Root.gameObject.activeSelf == value) return;
            p.Root.gameObject.SetActive(value);
            if (value) NpcFootstepSources.Register(p.Root);
            else NpcFootstepSources.Unregister(p.Root);
        }

        private void OnEnable()
        {
            lastMinute = double.NaN;
            allInside = false;
            if (participants == null) return;
            foreach (var p in participants)
                if (p.Root.gameObject.activeInHierarchy) NpcFootstepSources.Register(p.Root);
        }

        private void OnDisable()
        {
            if (participants == null) return;
            foreach (var p in participants)
                if (p?.Root != null) NpcFootstepSources.Unregister(p.Root);
        }
    }
}
