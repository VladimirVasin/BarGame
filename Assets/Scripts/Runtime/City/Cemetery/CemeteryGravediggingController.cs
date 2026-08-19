using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The gravedigger's job, from the watchman's offer to the open
    /// hole. It owns nothing the player can see except the marked-out
    /// plot and the grave itself: the offer lives on the watchman's
    /// own talk stub, and whether the job is taken lives in the quest
    /// log, so walking out of the city and back finds the work exactly
    /// as it was left.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryGravediggingController : MonoBehaviour
    {
        public const string RuntimeRootName = "Cemetery Gravedigging";

        private CemeteryGravediggingPlan plan;
        private CityCemeteryGroundExcavation excavation;
        private CemeteryGraveDigSiteInteraction site;
        private GameObject grave;

        /// <summary>The job as planned, present or not.</summary>
        public CemeteryGravediggingPlan Plan => plan;

        /// <summary>True when this city has a watchman with a vacant
        /// plot to point at.</summary>
        public bool HasJob => plan != null && plan.IsPresent;

        /// <summary>True while the old man still has the job to give.
        /// A refusal does not spend it — he offers again next time.
        /// </summary>
        public bool CanOffer =>
            HasJob &&
            GameSessionState.GetQuestStatus(QuestId.DigTheGrave) ==
                QuestStatus.NotStarted;

        public bool IsAccepted =>
            HasJob &&
            GameSessionState.IsQuestActive(QuestId.DigTheGrave);

        /// <summary>True once the hole is actually open.</summary>
        public bool IsDug => grave != null;

        /// <summary>The marked-out plot, while one is standing.
        /// </summary>
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
                !GameSessionState.TryActivateQuest(
                    QuestId.DigTheGrave))
            {
                return false;
            }

            RaiseSite();
            GameLog.Info(
                "city",
                "cemetery_gravedigging_accepted",
                GameLog.Field("plot", plan.Plot.StableId));
            return true;
        }

        /// <summary>
        /// Digs it: the ground gives up the rectangle, the grave is
        /// dressed into the hole and the job is done. False when the
        /// job was never taken, is already dug, or the ground refused
        /// the cut.
        /// </summary>
        public bool TryDig()
        {
            if (!IsAccepted || IsDug || !Excavate())
            {
                return false;
            }

            GameSessionState.TryCompleteQuest(QuestId.DigTheGrave);
            GameLog.Info(
                "city",
                "cemetery_grave_dug",
                GameLog.Field("plot", plan.Plot.StableId));
            return true;
        }

        /// <summary>
        /// Puts the world back where the quest log says it should be:
        /// an accepted job is marked out again, a finished one is
        /// already a hole in the ground. Runs on every city build, so
        /// the work survives a trip indoors.
        /// </summary>
        private void Restore()
        {
            if (!HasJob)
            {
                return;
            }

            switch (GameSessionState.GetQuestStatus(
                        QuestId.DigTheGrave))
            {
                case QuestStatus.Active:
                    RaiseSite();
                    break;
                case QuestStatus.Completed:
                    Excavate();
                    break;
            }
        }

        private void RaiseSite()
        {
            if (site != null || IsDug)
            {
                return;
            }

            site = CemeteryGraveDigSiteInteraction.Create(
                transform,
                plan,
                TryDig);
        }

        private bool Excavate()
        {
            if (excavation == null ||
                !excavation.Excavate(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        plan)))
            {
                return false;
            }

            grave = CityCemeteryPitWorldBuilder.Build(transform, plan);
            if (site != null)
            {
                // The marker is a promise the hole has now kept.
                if (Application.isPlaying)
                {
                    Destroy(site.gameObject);
                }
                else
                {
                    DestroyImmediate(site.gameObject);
                }

                site = null;
            }

            return true;
        }
    }
}
