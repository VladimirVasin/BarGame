using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeAlarmClockBuilder
    {
        internal const string OccluderId =
            "home.furniture.alarm-clock";

        private const float MinimumVisibility = 0.23f;

        private static readonly Color NightstandWood =
            new Color(0.105f, 0.055f, 0.035f);
        private static readonly Color NightstandEdge =
            new Color(0.19f, 0.095f, 0.052f);
        private static readonly Color ClockBody =
            new Color(0.075f, 0.068f, 0.062f);
        private static readonly Color ClockFace =
            new Color(0.012f, 0.008f, 0.008f);
        private static readonly Color DisplayRed =
            new Color(1f, 0.055f, 0.018f);

        public static HomeAlarmClock Build(
            Transform room,
            HomeAlarmClockPlan plan)
        {
            return Build(room, plan, null);
        }

        internal static HomeAlarmClock Build(
            Transform room,
            HomeAlarmClockPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            if (room == null)
            {
                throw new System.ArgumentNullException(nameof(room));
            }

            var occluderParts = new List<GameObject>();
            BuildNightstand(room, plan, occluderParts);

            GameObject alarmObject =
                new GameObject("Home Alarm Clock");
            alarmObject.transform.SetParent(room, false);
            alarmObject.transform.localPosition =
                plan.ClockPosition;

            occluderParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Alarm Clock Body",
                alarmObject.transform,
                Vector3.zero,
                HomeAlarmClockPlan.ClockBodySize,
                ClockBody,
                false));
            occluderParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Alarm Clock Face",
                alarmObject.transform,
                new Vector3(0f, 0f, -0.126f),
                new Vector3(0.395f, 0.175f, 0.018f),
                ClockFace,
                false));
            occluderParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Alarm Clock Snooze",
                alarmObject.transform,
                new Vector3(0f, 0.153f, 0.015f),
                new Vector3(0.16f, 0.035f, 0.075f),
                NightstandEdge,
                false));
            GameObject[] displaySegments =
                BuildDisplay(
                    alarmObject.transform,
                    out GameObject[] displayPunctuation);
            HomeAlarmClock alarm =
                alarmObject.AddComponent<HomeAlarmClock>();
            alarm.ConfigureDisplay(
                displaySegments,
                displayPunctuation);
            occlusionRegistry?.Register(
                OccluderId,
                HomeOccluderKind.FurnitureBlocker,
                MinimumVisibility,
                occluderParts.ToArray());
            return alarm;
        }

        private static void BuildNightstand(
            Transform room,
            HomeAlarmClockPlan plan,
            List<GameObject> occluderParts)
        {
            occluderParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Alarm Clock Nightstand",
                room,
                plan.NightstandCenter,
                plan.NightstandSize,
                NightstandWood,
                false));
            occluderParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Alarm Clock Nightstand Top",
                room,
                new Vector3(
                    plan.NightstandCenter.x,
                    HomeAlarmClockPlan.NightstandHeight +
                    HomeAlarmClockPlan.NightstandTopThickness *
                    0.5f,
                    plan.NightstandCenter.z),
                new Vector3(
                    plan.NightstandSize.x + 0.05f,
                    HomeAlarmClockPlan.NightstandTopThickness,
                    plan.NightstandSize.z + 0.04f),
                NightstandEdge,
                false));
            occluderParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Alarm Clock Nightstand Handle",
                room,
                new Vector3(
                    plan.NightstandCenter.x,
                    0.48f,
                    plan.NightstandCenter.z -
                    plan.NightstandSize.z * 0.51f),
                new Vector3(0.21f, 0.035f, 0.035f),
                new Color(0.27f, 0.17f, 0.09f),
                false));
        }

        private static GameObject[] BuildDisplay(
            Transform parent,
            out GameObject[] punctuation)
        {
            float[] digitCenters =
            {
                -0.135f,
                -0.075f,
                0.075f,
                0.135f
            };
            var segments = new GameObject[
                HomeAlarmClock.DisplayDigitCount *
                HomeAlarmClock.SegmentsPerDigit];
            for (int digit = 0;
                 digit < HomeAlarmClock.DisplayDigitCount;
                 digit++)
            {
                BuildDigitSegments(
                    parent,
                    digit,
                    digitCenters[digit],
                    segments);
            }

            punctuation = new GameObject[
                HomeAlarmClock.DisplayPunctuationCount];
            punctuation[0] = CreateDisplaySegment(
                "Alarm Clock Colon Upper",
                parent,
                new Vector3(0f, 0.024f, -0.139f),
                new Vector3(0.010f, 0.010f, 0.010f));
            punctuation[1] = CreateDisplaySegment(
                "Alarm Clock Colon Lower",
                parent,
                new Vector3(0f, -0.024f, -0.139f),
                new Vector3(0.010f, 0.010f, 0.010f));
            return segments;
        }

        private static void BuildDigitSegments(
            Transform parent,
            int digit,
            float centerX,
            GameObject[] displaySegments)
        {
            Vector3[] positions =
            {
                new Vector3(0f, 0.050f, -0.139f),
                new Vector3(0.024f, 0.027f, -0.139f),
                new Vector3(0.024f, -0.027f, -0.139f),
                new Vector3(0f, -0.050f, -0.139f),
                new Vector3(-0.024f, -0.027f, -0.139f),
                new Vector3(-0.024f, 0.027f, -0.139f),
                new Vector3(0f, 0f, -0.139f)
            };
            Vector3 horizontal =
                new Vector3(0.040f, 0.008f, 0.010f);
            Vector3 vertical =
                new Vector3(0.008f, 0.036f, 0.010f);

            for (int segment = 0;
                 segment < positions.Length;
                 segment++)
            {
                Vector3 position = positions[segment];
                position.x += centerX;
                displaySegments[
                    digit *
                    HomeAlarmClock.SegmentsPerDigit +
                    segment] = CreateDisplaySegment(
                    $"Alarm Clock Digit {digit} Segment {segment}",
                    parent,
                    position,
                    segment == 0 ||
                    segment == 3 ||
                    segment == 6
                        ? horizontal
                        : vertical);
            }
        }

        private static GameObject CreateDisplaySegment(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size)
        {
            return RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                position,
                size,
                DisplayRed,
                CityNightResources.EmissiveMaterial,
                false);
        }
    }
}
