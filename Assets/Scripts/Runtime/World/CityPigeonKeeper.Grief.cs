using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPigeonKeeper
    {
        public bool IsCrying { get; private set; }
        public float CryElapsed { get; private set; }
        public float CryDuration { get; private set; }
        public float CryWeight { get; private set; }
        public int GriefCount { get; private set; }
        public double NextGriefAt { get; private set; }
        public double LastCryActivityTime { get; private set; } = double.NegativeInfinity;
        public Vector3 CryFaceTarget { get; private set; }
        public float CryContactError { get; private set; }

        private CityPigeonKeeperSpeech griefSpeech;
        private Transform[] griefBones;
        private Quaternion[] griefBaseRotations;
        private Transform griefRightHand;
        private AnatomicalHandFrame griefRightAnatomy;
        private uint griefRandom;
        private bool griefPoseApplied, griefKeepsBroom, griefScheduleReady;

        internal void BindGriefSpeech(CityPigeonKeeperSpeech speech) => griefSpeech = speech;

        private void ResetGriefSchedule()
        {
            RestoreGriefAdditive();
            IsCrying = false;
            CryElapsed = CryWeight = CryContactError = 0f;
            griefRandom = unchecked((uint)GameSessionState.CitySeed * 397u +
                (uint)GameSessionState.GameDayIndex * 131u + 0x53719u);
            if (griefRandom == 0u) griefRandom = 1u;
            NextGriefAt = DayActivitySeconds + GriefInterval(120f, 180f);
            griefScheduleReady = true;
        }

        private float GriefInterval(float minimum, float maximum)
        {
            griefRandom ^= griefRandom << 13;
            griefRandom ^= griefRandom >> 17;
            griefRandom ^= griefRandom << 5;
            return Mathf.Lerp(minimum, maximum, (griefRandom & 0x00ffffffu) / 16777215f);
        }

        private bool AdvanceGriefClock(float delta)
        {
            if (!griefScheduleReady) ResetGriefSchedule();
            if (IsCrying)
            {
                dayElapsed = Mathf.Max(0f, dayElapsed - delta);
                CryElapsed = Mathf.Min(CryDuration, CryElapsed + delta);
                CryWeight = Ease(CryElapsed, 0f, 2f) * (1f - Ease(CryElapsed, CryDuration - 2f, CryDuration));
                LastCryActivityTime = DayActivitySeconds;
                if (CryElapsed >= CryDuration)
                {
                    IsCrying = false;
                    CryWeight = 0f;
                    NextGriefAt = DayActivitySeconds + GriefInterval(240f, 360f);
                }
                return true;
            }
            if (DayActivitySeconds < NextGriefAt || IsAtHome || IsCommuting || IsUsingGate ||
                griefSpeech && !griefSpeech.CanStartGrief) return false;
            foreach (DayLeg leg in dayLegs)
            {
                float workStart = leg.Start + leg.WalkSeconds;
                float at = dayElapsed - workStart;
                if (at < 0f || dayElapsed >= leg.End) continue;
                // Emotional pauses stay in the yard; upstairs is a short visit.
                if (leg.Stop.Task == CityPigeonKeeperDayTask.ReturnToDeck ||
                    leg.Stop.Task == CityPigeonKeeperDayTask.DeckRail || at > .35f) return false;
                dayElapsed = workStart;
                if (griefBones == null) BindGriefRig();
                griefKeepsBroom = true;
                IsCrying = true;
                CryElapsed = CryWeight = 0f;
                CryDuration = GriefInterval(10f, 14f);
                GriefCount++;
                LastCryActivityTime = DayActivitySeconds;
                return true;
            }
            return false;
        }

        private void BindGriefRig()
        {
            griefBones = new[] { spine, Require(Worker.ModelRoot, "chest"), Worker.Head,
                Require(Worker.ModelRoot, "clavicle.L"), Require(Worker.ModelRoot, "clavicle.R") };
            griefBaseRotations = new Quaternion[griefBones.Length];
            griefRightHand = Require(Worker.ModelRoot, "hand.R");
            griefRightAnatomy = fingers.GetAnatomicalFrame(false);
        }

        private void ApplyGriefPose(bool pose)
        {
            if (!pose || !IsCrying) return;
            for (int index = 0; index < griefBones.Length; index++)
                griefBaseRotations[index] = griefBones[index].localRotation;
            griefPoseApplied = true;
            float sob = Mathf.Max(0f, Mathf.Sin(CryElapsed * 4.1f + .65f * Mathf.Sin(CryElapsed * .83f)));
            sob = sob * sob * sob;
            Vector3 axis = Worker.transform.right;
            Quaternion headBase = griefBones[2].rotation;
            griefBones[0].rotation = Quaternion.AngleAxis((5f + 1.1f * sob) * CryWeight, axis) * griefBones[0].rotation;
            griefBones[1].rotation = Quaternion.AngleAxis((10f + 1.6f * sob) * CryWeight, axis) * griefBones[1].rotation;
            griefBones[2].rotation = Quaternion.AngleAxis((24f + .7f * Mathf.Sin(CryElapsed * 11.3f)) * CryWeight, axis) * griefBones[2].rotation;
            griefBones[3].rotation = Quaternion.AngleAxis(2.4f * sob * CryWeight, Worker.transform.forward) * griefBones[3].rotation;
            griefBones[4].rotation = Quaternion.AngleAxis(-2.4f * sob * CryWeight, Worker.transform.forward) * griefBones[4].rotation;

            Quaternion faceTilt = griefBones[2].rotation * Quaternion.Inverse(headBase);
            Vector3 up = faceTilt * Worker.transform.up;
            Vector3 front = faceTilt * Worker.transform.forward;
            CryFaceTarget = Worker.Head.position + up * .11f + front * .085f + Worker.transform.right * .045f;
            Quaternion palmRotation = griefRightAnatomy.ResolveHandRotation(-front, up);
            Vector3 gripInHand = Quaternion.Inverse(griefRightHand.rotation) *
                (Worker.RightGrip.position - griefRightHand.position);
            Vector3 socket = CryFaceTarget + palmRotation * (gripInHand - griefRightAnatomy.PalmCentreInHand);
            Worker.ApplyHandContacts(socket, null, palmRotation, null, CryWeight);
            fingers.SetGrip(false, 0f);
            if (griefKeepsBroom)
            {
                Pose hand = fingers.GetSocketPose(true, broom.position, Vector3.up, -Worker.transform.right);
                Worker.ApplyHandContacts(null, hand.position, null, hand.rotation, 1f);
                fingers.SetGrip(true, .85f);
            }
            Vector3 palm = griefRightHand.position + griefRightHand.rotation * griefRightAnatomy.PalmCentreInHand;
            CryContactError = CryWeight >= .999f ? Vector3.Distance(palm, CryFaceTarget) : 0f;
        }

        private void RestoreGriefAdditive()
        {
            if (!griefPoseApplied) return;
            for (int index = 0; index < griefBones.Length; index++) griefBones[index].localRotation = griefBaseRotations[index];
            griefPoseApplied = false;
        }

        private void CancelGrief()
        {
            RestoreGriefAdditive();
            IsCrying = false;
            CryWeight = CryContactError = 0f;
            griefScheduleReady = false;
        }
    }
}
