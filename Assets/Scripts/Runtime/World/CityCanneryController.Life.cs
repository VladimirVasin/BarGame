using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private static readonly float[] LifePeriods = { 37f, 31f, 43f, 47f };
        private static readonly float[] LifeOffsets = { 17f, 5f, 26f, 36f };
        private static readonly string[] WorkPoseBones = { "pelvis", "spine", "chest", "neck", "head",
            "upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R",
            "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
        private readonly bool[] factoryDutyBusy = new bool[4];
        private readonly float[] factorySecondaryWeight = new float[4];
        private readonly Transform[,] factoryPoseBones = new Transform[4, WorkPoseBones.Length];
        private readonly Vector3[] factoryIdlePositions = new Vector3[WorkPoseBones.Length];
        private readonly Quaternion[] factoryIdleRotations = new Quaternion[WorkPoseBones.Length];
        private readonly Vector3[] factoryRestDocks = new Vector3[3];
        private Transform receivingNeedle, preparationCloth;
        private Quaternion receivingNeedleRest, preparationClothRest;
        private Vector3 preparationClothDock;
        public double LifeSeconds { get; private set; }
        public CityCanneryConversationController FactoryConversation { get; private set; }
        public float ReceivingScaleWeight => ShippingScaleWeight;
        public bool PreparationClothInContact { get; private set; }

        public VillageResidentPresentation GetFactoryWorker(int index) =>
            index >= 0 && index < 4 && workers != null ? workers[index] : null;

        public bool FactoryWorkerHandsFree(int index) => index >= 0 && index < 4 &&
            FactoryPresentationActive && !factoryDutyBusy[index] && factorySecondaryWeight[index] < .001f;

        /// <summary>Real seconds until a reserved task or the next small rest action.
        /// A whole exchange must fit here before either speaker starts it.</summary>
        public double FactoryWorkerAvailableSeconds(int index)
        {
            if (index < 0 || index >= 4 || !FactoryPresentationActive) return 0d;
            if (factoryShiftWalking[index]) return 0d;
            if (index == 0 && Snapshot.Inspection.IsActive) return 0d;
            if (factoryDutyBusy[index])
                return index == 0 ? Math.Max(0d, Snapshot.Duration - Snapshot.Seconds) :
                    Math.Max(0d, (Production.Duration - Production.Seconds) / CityFishSupplyCycle.ProductionSpeed);
            if (factorySecondaryWeight[index] > .001f) return 0d;
            if (factoryWaitingOutside[index])
            {
                double nextEntry = FactoryShiftEntryTime(index);
                if (WorkingSeconds >= nextEntry)
                    nextEntry = Cycle.StageStart(CityFishSupplyStage.UnloadFish, Snapshot.Batch + 1) + EntryDelay(index);
                return Math.Max(0d, nextEntry - WorkingSeconds);
            }
            float phase = LifePhase(index);
            double untilAction = phase >= 9f ? LifePeriods[index] - phase : 0d;
            return Math.Max(0d, Math.Min(untilAction, FactoryDutyDistance(index, futureOnly: true) - 2d));
        }

        /// <summary>Reconstruct a living pose for captures, without advancing
        /// deliveries, replaying speech, or changing any cargo ownership.</summary>
        public void ApplyLifeAt(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            LifeSeconds = seconds;
            if (IsInitialized) ApplyWorkers();
        }

        private void CreateFactoryLife()
        {
            for (int role = 0; role < 4; role++)
                for (int bone = 0; bone < WorkPoseBones.Length; bone++)
                    factoryPoseBones[role, bone] = Require(workers[role].ModelRoot, WorkPoseBones[bone]);
            factoryRestDocks[0] = Anchor("ReceiverRestWorker");
            factoryRestDocks[1] = Anchor("PreparationTidyWorker");
            factoryRestDocks[2] = Anchor("SeamerRestWorker");
            receivingNeedle = Require(equipment, "MOVE_ScaleNeedle");
            receivingNeedleRest = receivingNeedle.localRotation;
            preparationCloth = Require(equipment, "MOVE_PreparationCloth");
            preparationClothDock = preparationCloth.position;
            preparationClothRest = preparationCloth.rotation;
            CreateFactoryShift();
        }

        private void ResetFactoryLifePose()
        {
            Array.Clear(factoryDutyBusy, 0, factoryDutyBusy.Length);
            Array.Clear(factorySecondaryWeight, 0, factorySecondaryWeight.Length);
            PreparationClothInContact = false;
            ShippingScaleWeight = 0f;
            InspectionBoxInHands = false;
            if (receivingNeedle != null) receivingNeedle.localRotation = receivingNeedleRest;
            if (preparationCloth != null)
                preparationCloth.SetPositionAndRotation(preparationClothDock, preparationClothRest);
        }

        private float LifePhase(int index) => (float)((LifeSeconds + LifeOffsets[index]) % LifePeriods[index]);

        private double FactoryDutyDistance(int role, bool futureOnly = false)
        {
            double distance = FactoryShiftBoundaryDistance(role, futureOnly);
            if (role == 0)
            {
                Reserve(Cycle.StageStart(CityFishSupplyStage.UnloadFish, Snapshot.Batch),
                    Cycle.StageDuration(CityFishSupplyStage.UnloadFish, Snapshot.Batch));
                Reserve(Cycle.StageStart(CityFishSupplyStage.UnloadFish, Snapshot.Batch + 1),
                    Cycle.StageDuration(CityFishSupplyStage.UnloadFish, Snapshot.Batch + 1));
                for (long batch = Snapshot.Batch; batch <= Snapshot.Batch + 1; batch++)
                    Reserve(Cycle.InspectionStart(0, batch),
                        Cycle.InspectionFinishedAt(batch) - Cycle.InspectionStart(0, batch));
                return distance;
            }
            // Include the next batch so a rest never straddles its first job.
            for (long batch = Snapshot.Batch; batch <= Snapshot.Batch + 1; batch++)
            for (int lot = 0; lot < Cycle.ProductionLotCount; lot++)
            {
                if (role == 1) Stage(CityCanneryProductionStage.Prepare);
                else if (role == 2)
                {
                    Stage(CityCanneryProductionStage.Fill);
                    Stage(CityCanneryProductionStage.Seal);
                }
                else
                {
                    Stage(CityCanneryProductionStage.LoadRetort);
                    double start = Cycle.ProductionStageStart(CityCanneryProductionStage.Cool, lot, batch);
                    double end = Cycle.ProductionStageStart(CityCanneryProductionStage.Pack, lot, batch) +
                        Cycle.ProductionStageDuration(CityCanneryProductionStage.Pack) + 6d;
                    Reserve(start, end - start);
                }
                void Stage(CityCanneryProductionStage stage) =>
                    Reserve(Cycle.ProductionStageStart(stage, lot, batch), Cycle.ProductionStageDuration(stage));
            }
            return distance;

            void Reserve(double start, double duration)
            {
                double end = start + duration;
                if (WorkingSeconds >= start && WorkingSeconds <= end) distance = 0d;
                else if (WorkingSeconds < start) distance = Math.Min(distance, start - WorkingSeconds);
                else if (!futureOnly) distance = Math.Min(distance, WorkingSeconds - end);
            }
        }

        private void ApplyReceiver()
        {
            if (ApplyInspectionReceiver()) return;
            VillageResidentPresentation actor = workers[0];
            Vector3 target = Anchor("RawDoor") + Vector3.up;
            StandWorker(actor, Anchor("Receiver"), Plan.Forward, target);
            bool receiving = Snapshot.Stage == CityFishSupplyStage.UnloadFish;
            if (receiving)
            {
                factoryDutyBusy[0] = true;
                Transform load = fish[ActiveUnit];
                if (load.gameObject.activeSelf) target = load.position + Vector3.up * .5f;
                ApplyCrewLook(actor, target, .85f);
            }
            else ApplyFactoryIdleLife(0, Anchor("Receiver"), Plan.Forward, target);
        }

        private void ApplyFactoryIdleLife(int role, Vector3 home, Vector3 forward, Vector3 ordinaryLook)
        {
            VillageResidentPresentation actor = workers[role];
            double dutyDistance = FactoryDutyDistance(role);
            float available = Ease((float)dutyDistance / 2f);
            float phase = LifePhase(role);
            float restWindow = CrewWorkWindow(phase, 0f, 9f, 1.7f);
            float away = restWindow * available;
            float task = CrewWorkWindow(phase, 2.3f, 6.7f, .9f) * available;
            factorySecondaryWeight[role] = away;
            Vector3 rest = role < 3 ? factoryRestDocks[role] : home;
            if (role < 3 && away > .001f)
            {
                actor.transform.position = Vector3.Lerp(home, rest, away);
                float derivative = phase < 1.7f ? 6f * (phase / 1.7f) * (1f - phase / 1.7f) / 1.7f :
                    phase > 7.3f && phase < 9f ? -6f * ((9f - phase) / 1.7f) * (1f - (9f - phase) / 1.7f) / 1.7f : 0f;
                float dutyRamp = Mathf.Clamp01((float)dutyDistance / 2f);
                float towardDuty = Math.Abs(FactoryDutyDistance(role, true) - dutyDistance) < .001d ? -1f : 1f;
                float workRate = IsBlocked || AutoAdvance && !CityFishSupplySession.HasStarted ? 0f :
                    Snapshot.IsDriving ? movementRate : 1f;
                derivative = derivative * available + restWindow * 3f * dutyRamp * (1f - dutyRamp) * towardDuty * workRate;
                float speed = Mathf.Abs(derivative) * Vector3.Distance(home, rest);
                if (speed > .01f)
                {
                    Vector3 step = (rest - home) * Mathf.Sign(derivative);
                    bool backwards = Vector3.Dot(step, forward) < -.01f;
                    Quaternion face = Quaternion.LookRotation(backwards ? -step : step, Vector3.up);
                    float turning = Mathf.Clamp01(speed / .18f);
                    actor.transform.rotation = Quaternion.Slerp(Quaternion.LookRotation(forward), face, turning);
                    actor.ApplyLocomotion(speed, false, (float)((backwards ? 120d - LifeSeconds % 120d : LifeSeconds % 120d) + role * .67d));
                }
            }
            // Independent low-amplitude breathing and weight shifts act above
            // the planted feet. They remain alive when the lorry has to wait.
            float time = (float)(LifeSeconds % 1000d) + role * 3.41f;
            Transform spine = workerSpines[role];
            spine.rotation = Quaternion.AngleAxis((.65f * Mathf.Sin(time * (1.05f + role * .08f)) + task * (role == 1 ? 7f : 2f)) * available,
                actor.transform.right) * spine.rotation;
            spine.rotation = Quaternion.AngleAxis(.65f * Mathf.Sin(time * .37f) * available, actor.transform.forward) * spine.rotation;
            Vector3 look = ordinaryLook;
            if (role == 0)
                look = Vector3.Lerp(ordinaryLook, Anchor("RawDoor") + Vector3.up * 1.1f,
                    CrewWorkWindow((time + 4f) % 19f, 2f, 8f, 2f) * available);
            else if (role == 1)
            {
                Vector3 dock = Anchor("PreparationTidyHand");
                float wipe = Mathf.Sin(Mathf.Clamp01((phase - 3.2f) / 2.6f) * Mathf.PI * 4f) * .065f * task;
                preparationCloth.position = preparationClothDock + Plan.Forward * wipe;
                Vector3 hand = preparationCloth.position + Vector3.up * .012f;
                ApplyCrewContacts(actor, hand, null, task);
                PreparationClothInContact = task >= .999f;
                look = Vector3.Lerp(look, dock, task);
            }
            else if (role == 2 || role == 3)
            {
                // A brief cuff check (line operator) or wrist release (retort)
                // has a visible approach and return, with no extra prop stock.
                Vector3 left = actor.transform.TransformPoint(new Vector3(-.12f, role == 2 ? 1.14f : 1.04f, .28f));
                ApplyCrewContacts(actor, null, left, task);
                Transform forearm = factoryPoseBones[role, 6];
                Vector3 touch = Vector3.Lerp(forearm.position, actor.LeftGrip.position, role == 2 ? .72f : .92f);
                ApplyCrewContacts(actor, touch, null, task);
                look = Vector3.Lerp(look, left, task);
            }
            ApplyCrewLook(actor, look, .55f + .25f * task);
        }

        private void BlendFactoryWorkPose(int role, float weight)
        {
            for (int i = 0; i < WorkPoseBones.Length; i++)
            {
                factoryIdlePositions[i] = factoryPoseBones[role, i].localPosition;
                factoryIdleRotations[i] = factoryPoseBones[role, i].localRotation;
            }
            workers[role].Apply(VillageResidentAction.StationWork, 1.1f * weight);
            for (int i = 0; i < WorkPoseBones.Length; i++)
            {
                Transform bone = factoryPoseBones[role, i];
                bone.localPosition = Vector3.Lerp(factoryIdlePositions[i], bone.localPosition, weight);
                bone.localRotation = Quaternion.Slerp(factoryIdleRotations[i], bone.localRotation, weight);
            }
        }
    }
}
