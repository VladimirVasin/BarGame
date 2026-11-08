using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCobblerWorker
    {
        public const float LampSwitchSeconds = 2f, LampPressSeconds = 1f;
        public bool WorkLightOn { get; private set; }
        public bool IsUsingLampSwitch { get; private set; }
        public float LampSwitchWeight { get; private set; }
        public float LampSwitchContactError { get; private set; }
        public event Action<bool> WorkLightChanged;
        private Transform lampKey, lampContact;
        private Vector3 lampKeyRest, fingertipInHand;
        private Quaternion lampPressRotation;
        private float buttonDepression;

        public static bool IsLampOnAt(double absoluteSeconds)
        {
            double day = absoluteSeconds % GameTimeState.RealSecondsPerGameDay;
            return day >= AtMinute(ShiftStartMinute) - LampSwitchSeconds + LampPressSeconds &&
                day < AtMinute(ShiftEndMinute) + LampPressSeconds;
        }

        private void ConfigureLampSwitch(Transform shell)
        {
            lampContact = CityCobblerWorkerPlan.Anchor(shell, "ANCHOR_LampSwitch");
            lampKey = CityCobblerWorkerPlan.Anchor(shell, "MOVE_LampSwitch");
            lampKeyRest = lampKey.position;
            Vector3 forward = site.Rotation * Vector3.forward;
            lampPressRotation = leftPalm.ResolveHandRotation(Vector3.down, (forward - .7f * Vector3.up).normalized);

            // The selected resident has an ungloved left hand. Measure the underside
            // of the real index fingertip in the pressing orientation once, in metres.
            var skin = CityCobblerWorkerPlan.Anchor(Worker.ModelRoot, "GEO_HandFinger0.L").GetComponent<SkinnedMeshRenderer>();
            Vector3[] vertices = CityPedestrianShoeContacts.SkinWorldVertices(skin);
            Quaternion intoHand = Quaternion.Inverse(leftHand.rotation);
            float bottom = float.PositiveInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = intoHand * (vertices[i] - leftHand.position);
                bottom = Mathf.Min(bottom, (lampPressRotation * vertices[i]).y);
            }
            int count = 0;
            foreach (Vector3 vertex in vertices)
                if ((lampPressRotation * vertex).y <= bottom + .002f) { fingertipInHand += vertex; count++; }
            if (count == 0) throw new InvalidOperationException("The cobbler's left index has no pressing surface.");
            fingertipInHand /= count;
        }

        private void SampleLampSwitch(double day, double opening, double closing)
        {
            double elapsed = day < opening ? day - (opening - LampSwitchSeconds) : day - closing;
            IsUsingLampSwitch = elapsed >= 0d && elapsed < LampSwitchSeconds;
            LampSwitchContactError = 0f;
            float t = (float)elapsed;
            LampSwitchWeight = IsUsingLampSwitch ? Smooth(.0f, .76f, t) * (1f - Smooth(1.35f, LampSwitchSeconds, t)) : 0f;
            float press = IsUsingLampSwitch ? Smooth(.78f, LampPressSeconds, t) * (1f - Smooth(1.15f, 1.35f, t)) : 0f;
            SetLampButton(press);
            bool on = IsLampOnAt(day);
            if (WorkLightOn != on)
            {
                WorkLightOn = on;
                WorkLightChanged?.Invoke(on);
            }
        }

        private void SetLampButton(float press)
        {
            if (!lampKey || press == buttonDepression) return;
            buttonDepression = press;
            lampKey.position = lampKeyRest + Vector3.down * (.004f * press);
        }

        private void ApplyLampSwitchPose()
        {
            if (LampSwitchWeight <= 0f) return;
            Vector3 press = lampContact.position;
            Quaternion intoHand = Quaternion.Inverse(leftHand.rotation);
            Vector3 socketOffset = intoHand * (Worker.LeftGrip.position - leftHand.position);
            Vector3 target = press + lampPressRotation * (socketOffset - fingertipInHand);
            Vector3 right = site.Rotation * Vector3.right;
            Quaternion upright = spine.rotation;
            for (int tilt = 20; tilt <= 60; tilt += 5)
            {
                spine.rotation = Quaternion.AngleAxis(tilt * LampSwitchWeight, right) * upright;
                if (Worker.ApplyHandContacts(null, target, null, lampPressRotation, LampSwitchWeight) ||
                    LampSwitchWeight < .999f) break;
            }
            fingers.SetGrip(true, 0f);
            if (LampSwitchWeight >= .999f)
                LampSwitchContactError = Vector3.Distance(leftHand.position + leftHand.rotation * fingertipInHand, press);
            HandContactError = LampSwitchContactError;
        }

        private static float Smooth(float from, float to, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
    }
}
