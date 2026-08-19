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
        /// lamp that stands at the end of the lake pier, set down at
        /// the head-end corner of the plot. It arrives with the job
        /// and stays until the earth is back — every act is worked by
        /// its light, and the first of them most of all. It is turned
        /// a little off the grave's own axis because it was put down
        /// by hand and not installed.
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
        private ICemeteryGraveWorkSession workSession;
        private CemeteryGraveDigSiteInteraction site;
        private GameObject pit;
        private GameObject lamp;
        private GameObject coffin;
        private GameObject mound;
        private GameObject stone;
        private GameObject spade;
        private GameObject waitingCoffin;
        private GameObject coffinBlocks;
        private GameObject lyingStone;
        private GameObject plaque;
        private CemeteryPlaqueReadInteraction plaqueReader;

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

        /// <summary>True once the earth is back and the mound is
        /// turned. The head of the grave is still bare.</summary>
        public bool IsFilled => Stage >= CemeteryGraveWorkStage.Filled;

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

        /// <summary>The register of holes this job cuts its grave out
        /// of. The work session needs it to open the ground before the
        /// act that records the opening is committed.</summary>
        public CityCemeteryGroundExcavation Excavation => excavation;

        /// <summary>
        /// Hands the acts over to something that makes the hero earn
        /// them. Without one the prompt commits an act outright, which
        /// is what every test and any headless build sees.
        /// </summary>
        public void SetWorkSession(ICemeteryGraveWorkSession session)
        {
            workSession = session;
        }

        /// <summary>
        /// Hides the lining of the open hole. The work session shows
        /// its own half-finished earth in the same place, and two
        /// meshes over one hole would fight for every pixel of it.
        /// </summary>
        public void SetPitDressingVisible(bool visible)
        {
            if (pit == null)
            {
                return;
            }

            Renderer[] renderers =
                pit.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                renderers[index].enabled = visible;
            }
        }

        /// <summary>
        /// The spade itself, for the act that swings it. There is only
        /// ever one, so an act borrows this rather than raising a
        /// second — which is the whole reason it lives here and not in
        /// the session.
        /// </summary>
        public CemeteryShovelAnimator Spade =>
            spade != null
                ? spade.GetComponent<CemeteryShovelAnimator>()
                : null;

        /// <summary>The board on the finished stone, for the shot
        /// that reads it.</summary>
        public Transform Plaque =>
            plaque != null ? plaque.transform : null;

        /// <summary>The coffin waiting on its blocks, for the act that
        /// carries it to the hole.</summary>
        public Transform WaitingCoffin =>
            waitingCoffin != null ? waitingCoffin.transform : null;

        /// <summary>
        /// The monument lying by the head, for the act that stands it
        /// up. There is only ever one stone: the act moves this rather
        /// than raising a second beside it.
        /// </summary>
        public Transform LyingStone =>
            lyingStone != null ? lyingStone.transform : null;

        /// <summary>Lays the monument back down where it was waiting.
        /// </summary>
        public void RestLyingStone()
        {
            if (lyingStone != null)
            {
                CityCemeterySealedGraveWorldBuilder.ApplyLyingPose(
                    lyingStone.transform,
                    plan,
                    1f);
            }
        }

        /// <summary>Puts the spade back where it stands between acts.
        /// </summary>
        public void RestSpade()
        {
            CemeteryShovelAnimator animator = Spade;
            if (animator == null)
            {
                return;
            }

            animator.Rest(
                CityGravediggerShovelWorldBuilder.GetRestPosition(
                    plan),
                CityGravediggerShovelWorldBuilder.GetRestRotation(
                    plan));
            animator.enabled = false;
        }

        /// <summary>Puts the coffin back on its blocks.</summary>
        public void RestWaitingCoffin()
        {
            if (waitingCoffin == null)
            {
                return;
            }

            waitingCoffin.transform.SetPositionAndRotation(
                CityCemeteryCoffinRestWorldBuilder.GetCoffinPosition(
                    plan),
                CityCemeteryCoffinRestWorldBuilder.GetCoffinRotation(
                    plan));
        }

        /// <summary>Hides the coffin already at the bottom of the
        /// hole, for an act that puts its own there.</summary>
        public void SetCoffinVisible(bool visible)
        {
            if (coffin == null)
            {
                return;
            }

            Renderer[] renderers =
                coffin.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                renderers[index].enabled = visible;
            }
        }

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
            // The tools come with the job too: a coffin waiting on its
            // blocks past the feet and a spade stood in the ground
            // beside it. The old man does not send a man to dig and
            // then make him find a box.
            RaiseKit();
            // The lamp comes with the job, not with the hole. A man
            // sent to dig at this hour is sent with a light, and the
            // first act is the one that most needs it: without it he
            // would be timing a spade against ground he cannot see.
            RaiseLamp();
            GameLog.Info(
                "city",
                "cemetery_gravedigging_accepted",
                GameLog.Field("plot", plan.Plot.StableId));
            return true;
        }

        /// <summary>
        /// Takes the next act of the work, whichever one is due: the
        /// ground gives up its rectangle, or the coffin goes down, or
        /// the earth goes back over it, or the stone goes up. False
        /// when there is no act due or the world refused it.
        ///
        /// This is the commit, not the doing of it. The minigame that
        /// makes the hero earn each act calls this once, at the end,
        /// with the work already visible on the ground.
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
                    return FillGrave();
                case CemeteryGraveWorkStage.Filled:
                    return RaiseStone();
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
                    RaiseLamp();
                    RaiseKit();
                    RaiseSite();
                    break;
                case CemeteryGraveWorkStage.Dug:
                    OpenPit();
                    RaiseLamp();
                    RaiseKit();
                    RaiseSite();
                    break;
                case CemeteryGraveWorkStage.Coffined:
                    OpenPit();
                    RaiseLamp();
                    RaiseCoffin();
                    RaiseSpade();
                    RaiseCoffinBlocks();
                    RaiseSite();
                    break;
                case CemeteryGraveWorkStage.Filled:
                    RaiseMound();
                    RaiseLamp();
                    RaiseSpade();
                    RaiseLyingStone();
                    RaiseSite();
                    break;
                case CemeteryGraveWorkStage.Sealed:
                case CemeteryGraveWorkStage.Paid:
                    RaiseMound();
                    RaiseStoneObject();
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

            // The box that was waiting is the box that is now down the
            // hole, so the waiting one goes — but its blocks stay
            // where they were kicked, because nobody carries those
            // away either.
            DestroyPart(ref waitingCoffin);
            site?.SetStage(CemeteryGraveWorkStage.Coffined);
            GameLog.Info(
                "city",
                "cemetery_grave_coffin_lowered",
                GameLog.Field("plot", plan.Plot.StableId));
            return true;
        }

        private bool FillGrave()
        {
            // The earth goes back before anything is built on it: a
            // mound standing over a hole that is still open would be
            // a worse bug than a step that simply refused.
            if (excavation == null ||
                !excavation.Fill(
                    CityCemeteryPitWorldBuilder.GetExcavationRect(
                        plan)) ||
                !GameSessionState.TryAdvanceGraveWork(
                    CemeteryGraveWorkStage.Filled))
            {
                return false;
            }

            DestroyPart(ref pit);
            DestroyPart(ref coffin);
            // The blocks go: the box is under the ground and nothing
            // else was ever set on them. The lamp and the spade stay —
            // there is one act left, it is worked by the same light and
            // driven home with the back of the same spade.
            DestroyPart(ref coffinBlocks);
            site?.SetStage(CemeteryGraveWorkStage.Filled);
            RaiseMound();
            GameLog.Info(
                "city",
                "cemetery_grave_filled",
                GameLog.Field("plot", plan.Plot.StableId));
            return true;
        }

        private bool RaiseStone()
        {
            RaiseStoneObject();
            if (stone == null ||
                !GameSessionState.TryAdvanceGraveWork(
                    CemeteryGraveWorkStage.Sealed))
            {
                return false;
            }

            // Now the site is finished, and everything that was here
            // to work with goes with him: the light he worked by, the
            // spade, and the ground where the stone was lying.
            DestroyPart(ref lamp);
            DestroyPart(ref spade);
            DestroyPart(ref lyingStone);
            // Told first, then taken down: in play mode the object
            // outlives this frame, and for that frame it must already
            // know it has nothing left to offer.
            site?.SetStage(CemeteryGraveWorkStage.Sealed);
            DestroyPart(ref site);
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
                RequestAdvance);
        }

        /// <summary>
        /// What the prompt actually calls. A work session that takes
        /// the act answers true and the act is now its business; the
        /// prompt reports nothing, because nothing has been done yet.
        /// With no session the act is committed on the spot, which is
        /// the behaviour the job shipped with.
        /// </summary>
        private bool RequestAdvance()
        {
            if (workSession != null && workSession.TryBegin(Stage))
            {
                return false;
            }

            return TryAdvance();
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

        /// <summary>
        /// The gear standing about the plot between acts: the coffin
        /// on its blocks and the spade in the ground beside it.
        /// </summary>
        private void RaiseKit()
        {
            RaiseSpade();
            RaiseLyingStone();
            RaiseCoffinBlocks();
            if (waitingCoffin != null)
            {
                return;
            }

            waitingCoffin = CityCemeteryCoffinWorldBuilder.Build(
                transform,
                plan);
            RestWaitingCoffin();
        }

        /// <summary>
        /// The two offcuts the box waits on. They outlive it: nobody
        /// carries blocks away, so they stay by the foot of the grave
        /// until the whole site is tidied at the last spadeful.
        /// </summary>
        /// <summary>
        /// The monument, on its back past the head of the grave. It is
        /// delivered with the job like everything else: the hero has to
        /// see the stone waiting before he is asked to stand it up.
        /// </summary>
        private void RaiseLyingStone()
        {
            if (lyingStone == null && stone == null)
            {
                lyingStone =
                    CityCemeterySealedGraveWorldBuilder
                        .BuildLyingStone(transform, plan);
            }
        }

        private void RaiseCoffinBlocks()
        {
            if (coffinBlocks == null)
            {
                coffinBlocks =
                    CityCemeteryCoffinRestWorldBuilder.Build(
                        transform,
                        plan);
            }
        }

        /// <summary>
        /// The one spade this job ever has. It is built once and lives
        /// until the earth is back, so the act that swings it moves
        /// this object rather than raising a second one beside it.
        /// </summary>
        private void RaiseSpade()
        {
            if (spade != null)
            {
                return;
            }

            spade = CityGravediggerShovelWorldBuilder.Build(
                transform,
                CityGravediggerShovelWorldBuilder.RootName);
            if (spade == null)
            {
                return;
            }

            var animator =
                spade.AddComponent<CemeteryShovelAnimator>();
            animator.Rest(
                CityGravediggerShovelWorldBuilder.GetRestPosition(
                    plan),
                CityGravediggerShovelWorldBuilder.GetRestRotation(
                    plan));
            animator.enabled = false;
        }

        private void RaiseMound()
        {
            if (mound == null)
            {
                mound = CityCemeterySealedGraveWorldBuilder.BuildMound(
                    transform,
                    plan);
            }
        }

        /// <summary>
        /// The stone is parented to the mound so a finished grave is
        /// still one object in the hierarchy, exactly as it was before
        /// filling and setting became separate acts.
        /// </summary>
        private void RaiseStoneObject()
        {
            RaiseMound();
            if (stone == null && mound != null)
            {
                stone = CityCemeterySealedGraveWorldBuilder
                    .BuildStandingStone(mound.transform, plan);
                DestroyPart(ref lyingStone);
            }

            RaisePlaque();
        }

        /// <summary>
        /// The board on the face of the stone, and the stub that reads
        /// it. Both outlive the worksite: the plaque carries no letters
        /// of its own, so without something to read it with the line
        /// the player wrote would be seen once and never again.
        /// </summary>
        private void RaisePlaque()
        {
            if (stone == null)
            {
                return;
            }

            if (plaque == null)
            {
                plaque = CityCemeteryPlaqueWorldBuilder.Attach(
                    stone.transform,
                    plan);
            }

            if (plaqueReader == null)
            {
                plaqueReader = CemeteryPlaqueReadInteraction.Create(
                    transform,
                    plan,
                    RaisePlaqueRead);
            }
        }

        /// <summary>Raised when the hero stops to read the board.
        /// </summary>
        public event Action PlaqueRead;

        private void RaisePlaqueRead()
        {
            PlaqueRead?.Invoke();
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
