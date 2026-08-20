using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Every grave the watchman has going at once.
    ///
    /// He used to have one hole and, once it was closed, nothing left
    /// to say. He has a yard: when a man comes back to the window
    /// there is always another plot, and the only limit is how many
    /// he will let one gravedigger hold open at a time.
    ///
    /// The register owns one <see cref="CemeteryGravediggingController"/>
    /// per grave — every job in the book of work, rebuilt on each city
    /// build so a half-dug hole and a finished stone both come back
    /// where they were left, plus the one he is about to offer, which
    /// stands ready with nothing on the ground until it is taken.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryGravediggingRegister :
        MonoBehaviour,
        ICemeteryWorkGiver
    {
        public const string RuntimeRootName = "Cemetery Graves";

        /// <summary>
        /// How many unfinished holes he will let one man hold. He is
        /// not sentimental about it — an open grave is an open grave —
        /// but he has been at this long enough to know that a fourth
        /// one is a yard full of holes and no graves.
        /// </summary>
        public const int MaximumOpenJobs = 3;

        private readonly List<CemeteryGravediggingController> jobs =
            new List<CemeteryGravediggingController>();
        private readonly HashSet<string> taken =
            new HashSet<string>(StringComparer.Ordinal);

        private CityCemeteryPlan cemeteryPlan;
        private CemeteryWatchmanPlan watchmanPlan;
        private CityCemeteryGroundExcavation excavation;
        private CemeteryGravediggingController pending;
        private ICemeteryGraveWorkSession workSession;

        /// <summary>Every grave standing in the yard: the ones being
        /// worked, the ones finished, and the one on offer.</summary>
        public IReadOnlyList<CemeteryGravediggingController> Jobs =>
            jobs;

        /// <summary>The grave he is offering, or null when he has
        /// none to give.</summary>
        public CemeteryGravediggingController Pending => pending;

        /// <summary>Raised for every job that comes into being, both
        /// the ones rebuilt on a city build and the ones taken while
        /// the hero is standing there.</summary>
        public event Action<CemeteryGravediggingController> JobRaised;

        /// <summary>True while this city has a cemetery with a
        /// watchman and a plot he can point at.</summary>
        public bool HasWork => jobs.Count > 0 || pending != null;

        /// <summary>How many holes are open: taken and not yet closed
        /// with a stone at the head.</summary>
        public int OpenJobCount =>
            GameSessionState.UnfinishedGraveWorkCount;

        /// <summary>
        /// True while he has a plot to hand over and the hero is not
        /// already carrying as much as he is allowed to. A refusal
        /// does not spend the offer — it is the same hole next time.
        /// </summary>
        public bool CanOffer =>
            pending != null &&
            pending.CanOffer &&
            OpenJobCount < MaximumOpenJobs;

        public static CemeteryGravediggingRegister Create(
            Transform parent,
            CityCemeteryPlan cityCemeteryPlan,
            CemeteryWatchmanPlan cemeteryWatchmanPlan,
            CityCemeteryGroundExcavation groundExcavation)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var root = new GameObject(RuntimeRootName);
            root.transform.SetParent(parent, false);
            var register =
                root.AddComponent<CemeteryGravediggingRegister>();
            register.cemeteryPlan = cityCemeteryPlan;
            register.watchmanPlan = cemeteryWatchmanPlan;
            register.excavation = groundExcavation;
            register.Restore();
            return register;
        }

        /// <summary>
        /// Hands the acts over to something that makes the hero earn
        /// them, for every grave here and every one still to come.
        /// </summary>
        public void SetWorkSession(ICemeteryGraveWorkSession session)
        {
            workSession = session;
            for (int index = 0; index < jobs.Count; index++)
            {
                jobs[index].SetWorkSession(session);
            }

            // The offer too, and it matters most: the session is
            // raised after this register is, so the very next grave
            // the hero takes is the one that would otherwise be
            // handed its acts instead of made to work them.
            pending?.SetWorkSession(session);
        }

        /// <summary>
        /// Takes whatever he is offering. The plot passes into the
        /// book of work, the tools land on it, and he turns round and
        /// finds the next free plot — so the answer to "have you got
        /// anything else" is yes until the yard is full.
        /// </summary>
        public bool TryAccept()
        {
            if (!CanOffer || !pending.TryAccept())
            {
                return false;
            }

            RaiseNextOffer();
            return true;
        }

        /// <summary>
        /// Settles up for every grave that is closed and unpaid, in
        /// the order they were dug, and answers the total. He counts
        /// out one sum rather than making a man come back three times
        /// for three holes he has already filled in.
        /// </summary>
        public int CollectWages()
        {
            int total = 0;
            for (int index = 0; index < jobs.Count; index++)
            {
                if (jobs[index].TryCollectWage())
                {
                    total += CemeteryGravediggingController.Wage;
                }
            }

            return total;
        }

        /// <summary>Whether any finished grave is still owed for.
        /// </summary>
        public bool IsOwedWages
        {
            get
            {
                for (int index = 0; index < jobs.Count; index++)
                {
                    if (jobs[index].CanCollectWage)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Stands the whole yard back up out of the book of work: one
        /// controller per grave he was ever sent to, in the order they
        /// were taken, and then the next offer on top.
        /// </summary>
        private void Restore()
        {
            IReadOnlyList<CemeteryGraveWorkRecord> records =
                GameSessionState.GraveWork;
            for (int index = 0; index < records.Count; index++)
            {
                string plotId = records[index].PlotId;
                CemeteryGravediggingPlan plan =
                    CemeteryGravediggingPlan.CreateFor(
                        cemeteryPlan,
                        plotId);
                if (!plan.IsPresent)
                {
                    // The seed that dug this grave is not the seed
                    // standing here. Nothing to rebuild, and nothing
                    // to be done about it either.
                    GameLog.Info(
                        "city",
                        "cemetery_gravedigging_plot_missing",
                        GameLog.Field("plot", plotId));
                    continue;
                }

                Adopt(RaiseJob(plan));
            }

            RaiseNextOffer();
        }

        /// <summary>
        /// Finds the next plot he can point at and stands a job over
        /// it, unspent. Nothing of it reaches the ground until the
        /// hero says yes.
        /// </summary>
        private void RaiseNextOffer()
        {
            if (pending != null)
            {
                Adopt(pending);
                pending = null;
            }

            CemeteryGravediggingPlan plan =
                CemeteryGravediggingPlan.Create(
                    cemeteryPlan,
                    watchmanPlan,
                    taken);
            if (!plan.IsPresent)
            {
                return;
            }

            pending = RaiseJob(plan);
        }

        private CemeteryGravediggingController RaiseJob(
            CemeteryGravediggingPlan plan)
        {
            CemeteryGravediggingController job =
                CemeteryGravediggingController.Create(
                    transform,
                    plan,
                    excavation);
            job.SetWorkSession(workSession);
            JobRaised?.Invoke(job);
            return job;
        }

        /// <summary>Files a job under its plot, so no later offer can
        /// point a second man at the same ground.</summary>
        private void Adopt(CemeteryGravediggingController job)
        {
            if (job == null || jobs.Contains(job))
            {
                return;
            }

            jobs.Add(job);
            taken.Add(job.PlotId);
        }
    }
}
