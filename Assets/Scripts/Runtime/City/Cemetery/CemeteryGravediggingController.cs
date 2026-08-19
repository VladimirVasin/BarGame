using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The gravedigger's job, from the watchman's offer to the wage.
    /// The work is three separate acts on the same plot — open the
    /// hole, lower the coffin into it, fill it in and set the stone —
    /// and only the last of them is a finished grave. Coming back to
    /// the old man afterwards is what turns it into money.
    ///
    /// It owns nothing the player can see except the plot: the offer
    /// lives on the watchman's own talk stub, and how far the work has
    /// got lives in <see cref="GameSessionState.GraveWorkStage"/>, so
    /// walking out of the city and back finds it exactly as it was
    /// left.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryGravediggingController : MonoBehaviour
    {
        public const string RuntimeRootName = "Cemetery Gravedigging";

        /// <summary>
        /// The lamp the gravedigger works by: the same kerosene hand
        /// lamp that stands at the end of the lake pier, set down on
        /// the collar beside the open hole. It is turned a little off
        /// the grave's own axis because it was put down by hand and
        /// not installed.
        /// </summary>
        public const string LampName = "Grave Work Lamp";
        public const float LampYawDegrees = 24f;

        /// <summary>
        /// What the old man pays for a grave. A day's work against a
        /// city where a bottle is `28` and the best cognac in the
        /// house is `25`.
        /// </summary>
        public const int Wage = 150;

        private CemeteryGravediggingPlan plan;
        private CityCemeteryGroundExcavation excavation;
        private CemeteryGraveDigSiteInteraction site;
        private GameObject pit;
        private GameObject lamp;
        private GameObject coffin;
        private GameObject sealedGrave;

        /// <summary>The job as planned, present or not.</summary>
        public CemeteryGravediggingPlan Plan => plan;

        /// <summary>True when this city has a watchman with a vacant
        /// plot to point at.</summary>
        public bool HasJob => plan != null && plan.IsPresent;

        /// <summary>How far the work has got. Without a job there is
        /// nothing to have got anywhere.</summary>
        public CemeteryGraveWorkStage Stage =>
            HasJob
                ? GameSessionState.GraveWorkStage
                : CemeteryGraveWorkStage.Unclaimed;

        /// <summary>True while the old man still has the job to give.
        /// A refusal does not spend it — he offers again next time.
        /// </summary>
        public bool CanOffer =>
            HasJob && Stage == CemeteryGraveWorkStage.Unclaimed;

        /// <summary>True once the hero has taken the work.</summary>
        public bool IsAccepted =>
            Stage >= CemeteryGraveWorkStage.Marked;

        /// <summary>True once the hole is actually open.</summary>
        public bool IsDug => Stage >= CemeteryGraveWorkStage.Dug;

        /// <summary>True once the coffin is down in it.</summary>
        public bool IsCoffined =>
            Stage >= CemeteryGraveWorkStage.Coffined;

        /// <summary>True only once the grave is closed and the stone
        /// is standing: the job, and nothing short of it.</summary>
        public bool IsSealed => Stage >= CemeteryGraveWorkStage.Sealed;

        /// <summary>True while the finished work is still unpaid.
        /// </summary>
        public bool CanCollectWage =>
            Stage == CemeteryGraveWorkStage.Sealed;

        public bool IsPaid => Stage == CemeteryGraveWorkStage.Paid;

        /// <summary>The worksite, while one is standing.</summary>
        public CemeteryGraveDigSiteInteraction Site => site;

        public static CemeteryGravediggingController Create(
            Transform parent,
            CemeteryGravediggingPlan gravediggingPlan,
            CityCemeteryGroundExcavation groundExcavation)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (gravediggingPlan == null)
            {
                throw new ArgumentNullException(
                    nameof(gravediggingPlan));
            }

            CemeteryGravediggingPlan.ValidateOrThrow(gravediggingPlan);

            var root = new GameObject(RuntimeRootName);
            root.transform.SetParent(parent, false);
            var controller =
                root.AddComponent<CemeteryGravediggingController>();
            controller.plan = gravediggingPlan;
            controller.excavation = groundExcavation;
            controller.Restore();
            return controller;
        }

        /// <summary>
        /// Takes the job: the quest goes into the log and the plot is
        /// marked out on the ground. False when there was nothing to
        /// take.
        /// </summary>
        public bool TryAccept()
        {
            if (!CanOffer ||
                !GameSessionState.TryAdvanceGraveWork(
                    CemeteryGraveWorkStage.Marked))
            {
                return false;
            }

            GameSessionState.TryActivateQuest(QuestId.DigTheGrave);
            RaiseSite();
            GameLog.Info(
                "city",
                "cemetery_gravedigging_accepted",
                GameLog.Field("plot", plan.Plot.StableId));
            return true;
        }

        /// <summary>
        /// Takes the next act of the work, whichever one is due: the
        /// ground gives up its rectangle, or the coffin goes down, or
        /// the earth goes back over it and the stone goes up. False
        /// when there is no act due or the world refused it.
        /// </summary>
        public bool TryAdvance()
        {
            switch (Stage)
            {
                case CemeteryGraveWorkStage.Marked:
                    return Dig();
                case CemeteryGraveWorkStage.Dug:
                    return LowerCoffin();
                case CemeteryGraveWorkStage.Coffined:
                    return SealGrave();
                default:
                    return false;
            }
        }

        /// <summary>
        /// Settles up for a finished grave. The world does not change:
        /// by the time he is paid the grave has been closed for as
        /// long as it took him to walk to the gate.
        /// </summary>
        public bool TryCollectWage()
        {
            if (!CanCollectWage ||
                !GameSessionState.TryEarnCash(
                    Wage,
                    "cemetery_gravedigging"))
            {
                return false;
            }

            GameSessionState.TryAdvanceGraveWork(
                CemeteryGraveWorkStage.Paid);
            GameLog.Info(
                "city",
                "cemetery_gravedigging_paid",
                GameLog.Field("plot", plan.Plot.StableId),
                GameLog.Field("wage", Wage));
            return true;
        }

        /// <summary>
        /// Puts the world back where the stage says it should be: a
        /// taken job is marked out again, an open hole is open with
        /// its lamp beside it and whatever is already down in it, and
        /// a finished grave is a mound with a stone at its head. Runs
        /// on every city build, so the work survives a trip indoors.
        /// </summary>
        private void Restore()
        {
            switch (Stage)
            {
                case CemeteryGraveWorkStage.Marked:
                    RaiseSite();
                    break;
                case CemeteryGraveWorkStage.Dug:
                    OpenPit();
                    RaiseLamp();
                    RaiseSite();
                    break;
                case CemeteryGraveWorkStage.Coffined:
                    OpenPit();
                    RaiseLamp();
                    RaiseCoffin();
                    RaiseSite();
                    break;
                case CemeteryGraveWorkStage.Sealed:
                case CemeteryGraveWorkStage.Paid:
                    RaiseMonument();
                    break;
            }
        }

        private bool Dig()
        {
            if (!OpenPit() ||
                !GameSessionState.TryAdvanceGraveWork(
                    CemeteryGraveWorkStage.Dug))
            {
                return false;
            }

            RaiseLamp();
            site?.SetStage(CemeteryGraveWorkStage.Dug);
            GameLog.Info(
                "city",
                "cemetery_grave_dug",
                GameLog.Field("plot", plan.Plot.StableId));
            return true;
        }

        private bool LowerCoffin()
        {
            RaiseCoffin();
            if (coffin == null ||
                !GameSessionState.TryAdvanceGraveWork(
                    CemeteryGraveWorkStage.Coffined))
            {
                return false;
            }

            site?.SetStage(CemeteryGraveWorkStage.Coffined);
            GameLog.Info(
                "city",
                "cemetery_grave_coffin_lowered",
                GameLog.Field("plot", plan.Plot.StableId));
            return true;
        }

        private bool SealGrave()
        {
            // The earth goes back before anything is built on it: a
            // stone standing over a hole that is still open would be
            // a worse bug than a step that simply refused.
            if (excavation == null ||
                !excavation.Fill(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        plan)) ||
                !GameSessionState.TryAdvanceGraveWork(
                    CemeteryGraveWorkStage.Sealed))
            {
                return false;
            }

            DestroyPart(ref pit);
            DestroyPart(ref coffin);
            // The lamp goes with them. It stood there because there was
            // a hole to see into, and there is no longer a hole.
            DestroyPart(ref lamp);
            // Told first, then taken down: in play mode the object
            // outlives this frame, and for that frame it must already
            // know it has nothing left to offer.
            site?.SetStage(CemeteryGraveWorkStage.Sealed);
            DestroyPart(ref site);
            RaiseMonument();
            GameSessionState.TryCompleteQuest(QuestId.DigTheGrave);
            GameLog.Info(
                "city",
                "cemetery_grave_sealed",
                GameLog.Field("plot", plan.Plot.StableId),
                GameLog.Field(
                    "monument",
                    plan.Monument.ToString()));
            return true;
        }

        private void RaiseSite()
        {
            if (site != null ||
                !CemeteryGraveDigSiteInteraction.IsWorkingStage(Stage))
            {
                return;
            }

            site = CemeteryGraveDigSiteInteraction.Create(
                transform,
                plan,
                Stage,
                TryAdvance);
        }

        private bool OpenPit()
        {
            if (pit != null)
            {
                return true;
            }

            if (excavation == null ||
                !excavation.Excavate(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        plan)))
            {
                return false;
            }

            pit = CityCemeteryPitWorldBuilder.Build(transform, plan);
            return pit != null;
        }

        private void RaiseLamp()
        {
            if (lamp == null)
            {
                lamp = CityHandLampWorldBuilder.Build(
                    transform,
                    LampName,
                    plan.LampGround,
                    plan.Heading.eulerAngles.y + LampYawDegrees);
            }
        }

        private void RaiseCoffin()
        {
            if (coffin == null)
            {
                coffin = CityCemeteryCoffinWorldBuilder.Build(
                    transform,
                    plan);
            }
        }

        private void RaiseMonument()
        {
            if (sealedGrave == null)
            {
                sealedGrave =
                    CityCemeterySealedGraveWorldBuilder.Build(
                        transform,
                        plan);
            }
        }

        private void DestroyPart(ref GameObject part)
        {
            if (part == null)
            {
                return;
            }

            GameObject doomed = part;
            part = null;
            if (Application.isPlaying)
            {
                Destroy(doomed);
            }
            else
            {
                DestroyImmediate(doomed);
            }
        }

        private void DestroyPart(
            ref CemeteryGraveDigSiteInteraction part)
        {
            if (part == null)
            {
                return;
            }

            GameObject doomed = part.gameObject;
            part = null;
            if (Application.isPlaying)
            {
                Destroy(doomed);
            }
            else
            {
                DestroyImmediate(doomed);
            }
        }
    }
}
