using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityGlassReturnWorker
    {
        public const float SwitchActionSeconds = 5f, SwitchPressSeconds = 1.6f, GrilleTravelSeconds = 3f;
        public bool WorkLightOn { get; private set; } = true;
        public event Action<bool> WorkLightChanged;
        public bool IsUsingSwitch { get; private set; }
        public float SwitchActionTime { get; private set; }
        public float SwitchContactWeight { get; private set; }
        public float SwitchContactError { get; private set; }
        public float WindowGrilleOpenness => windowGrille == null ? (WorkLightOn ? 1f : 0f) : 1f - windowGrille.ClosedAmount;
        public float TransitionTime { get; private set; }
        public float ClosingSwitchPressSeconds => switchWalkSeconds + SwitchPressSeconds;
        public float OpeningSwitchPressBeforeShift => switchWalkSeconds + SwitchActionSeconds - SwitchPressSeconds;

        private CityGlassReturnGrille windowGrille;
        private Transform switchRocker, switchContact;
        private Quaternion rockerRest;
        private bool operatingSeek = true;
        private double switchEventClock, grilleDelay;

        internal void BindWindowGrille(CityGlassReturnGrille value) => windowGrille = value;

        private void InitializeOperating()
        {
            switchRocker = CityGlassReturnAssetProvider.FindPart(transform, "MOVE_SwitchRocker");
            switchContact = CityGlassReturnAssetProvider.FindPart(switchRocker, "ANCHOR_SwitchContact");
            if (windowGrille == null) throw new InvalidOperationException("The glass worker requires its real receiving-window grille.");
            rockerRest = switchRocker.localRotation;
            SeekOperating();
        }

        private void SeekOperating()
        {
            operatingSeek = true; grilleDelay = 0d;
            IsUsingSwitch = false;
            SwitchActionTime = SwitchContactError = SwitchContactWeight = 0f;
        }

        private void SetPower(bool on)
        {
            if (WorkLightOn == on) return;
            WorkLightOn = on;
            WorkLightChanged?.Invoke(on);
        }

        private void SetRocker(float on)
        {
            if (switchRocker)
                switchRocker.localRotation = rockerRest * Quaternion.AngleAxis(Mathf.Lerp(-10f, 10f, on), Vector3.forward);
        }

        private void SampleOperatingRest(bool on)
        {
            SetPower(on);
            SetRocker(on ? 1f : 0f);
            TransitionTime = GrilleTravelSeconds;
            if (windowGrille != null) windowGrille.TrySetClosedAmount(on ? 0f : 1f, heroBody, forceSample || operatingSeek);
        }

        private void SampleOperatingTimeline(double seconds, float elapsed, bool returning, double delta)
        {
            float press = returning ? homeWalkSeconds + DoorSeconds * 2f + crossWalkSeconds +
                staffWalkSeconds + SwitchPressSeconds : ClosingSwitchPressSeconds;
            bool pressed = elapsed >= press;
            bool on = pressed ? returning : !returning;
            if (operatingSeek || forceSample)
            {
                SetPower(on);
                grilleDelay = 0d;
                switchEventClock = pressed ? seconds - (elapsed - press) : seconds - GrilleTravelSeconds;
            }
            else if (WorkLightOn != on)
            {
                // This physical press owns both consequences. The light changes
                // now; the motor samples its travel from exactly the same event.
                SetPower(on);
                switchEventClock = seconds - (elapsed - press);
                grilleDelay = 0d;
            }
            SetRocker(on ? 1f : 0f);
            float time = Mathf.Clamp((float)(seconds - switchEventClock - grilleDelay), 0f, GrilleTravelSeconds);
            float travel = Ease(time, 0f, GrilleTravelSeconds);
            float amount = on ? 1f - travel : travel;
            if (!windowGrille.TrySetClosedAmount(amount, heroBody, forceSample || operatingSeek))
            {
                grilleDelay += delta;
                time = Mathf.Clamp((float)(seconds - switchEventClock - grilleDelay), 0f, GrilleTravelSeconds);
            }
            TransitionTime = time;
        }

        private void UseSwitch(double seconds, float elapsed, bool on, double delta, bool pose)
        {
            IsUsingSwitch = true;
            SwitchActionTime = elapsed;
            Vector3 stand = CommutePlan.WorkToSwitch[CommutePlan.WorkToSwitch.Count - 1];
            Worker.transform.SetPositionAndRotation(stand, CommutePlan.SwitchFacing);
            float move = Ease(elapsed, SwitchPressSeconds - .28f, SwitchPressSeconds);
            SetRocker(on ? move : 1f - move);
            SwitchContactWeight = Ease(elapsed, 0f, .8f) * (1f - Ease(elapsed, 2.2f, 3f));
            // Do not abandon a stalled motor. The same sampled trip resumes as
            // soon as its real moving barrier can continue without crushing a body.
            if (elapsed >= SwitchActionSeconds - .25f &&
                (on ? WindowGrilleOpenness < .999f : WindowGrilleOpenness > .001f)) commuteDelay += delta;
            if (!pose) return;
            Worker.ApplyLocomotion(0f, false, (float)(seconds % 1200d), switchContact.position);
            Quaternion rotation = doorPalm.ResolveHandRotation(switchRocker.right, switchRocker.up);
            Vector3 socketInHand = Quaternion.Inverse(doorHand.rotation) * (Worker.RightGrip.position - doorHand.position);
            Vector3 target = switchContact.position + rotation * (socketInHand - doorPalm.PalmCentreInHand);
            Worker.ApplyHandContacts(target, null, rotation, null, SwitchContactWeight);
            hands.SetGrip(false, 0f);
            Vector3 palm = doorHand.position + doorHand.rotation * doorPalm.PalmCentreInHand;
            SwitchContactError = SwitchContactWeight > .999f ? Vector3.Distance(palm, switchContact.position) : 0f;
            HandContactError = SwitchContactError;
        }
    }
}
