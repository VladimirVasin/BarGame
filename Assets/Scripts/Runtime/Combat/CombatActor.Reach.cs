using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private int reachSequence = -1;
        private MeleeBufferedAction reachAction;
        private float attackReach;
        internal float AttackReach01 => attackReach;

        private MeleeBufferedAction ReachAction => State.IsShoving ? MeleeBufferedAction.Shove :
            State.IsKicking ? MeleeBufferedAction.Kick :
            (State.IsAttacking && reaction == null || State.IsCharging) ? MeleeBufferedAction.Attack : MeleeBufferedAction.None;

        private void UpdateAttackReach(bool allowOpeningUpdate)
        {
            MeleeBufferedAction action = ReachAction;
            bool changed = reachSequence != State.AttackSequence || reachAction != action;
            if (action == MeleeBufferedAction.None)
            { reachAction = action; reachSequence = -1; attackReach = 0f; return; }
            float elapsed = action == MeleeBufferedAction.Kick ? State.KickElapsed :
                action == MeleeBufferedAction.Shove ? State.ShoveElapsed : State.AttackElapsed;
            float opening = action == MeleeBufferedAction.Kick ? State.Settings.KickWindupSeconds * EarlyFacingFraction :
                action == MeleeBufferedAction.Shove ? State.Settings.ShoveContactSeconds : State.AttackWindupSeconds * EarlyFacingFraction;
            if (!changed && (!allowOpeningUpdate || !State.IsCharging && elapsed >= opening)) return;
            reachSequence = State.AttackSequence; reachAction = action;
            if (contactTarget == null) { attackReach = .5f; return; }
            Vector3 point = contactTarget.transform.position;
            if (contactTarget.Hurtboxes != null && contactTarget.Hurtboxes.ChestSurface(transform.position, transform.forward, out var chest))
                point = chest.Point;
            float gap = Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;
            // These are pose mixing ranges, never hit eligibility or extra reach.
            float near = action == MeleeBufferedAction.Kick ? .50f : action == MeleeBufferedAction.Shove ? .40f : .68f;
            float far = action == MeleeBufferedAction.Kick ? .95f : action == MeleeBufferedAction.Shove ? .85f : 1.30f;
            attackReach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(near, far, gap));
        }

        private void ConfigureAttackReachPose(MeleeBufferedAction action, float progress = -1f)
        {
            float amount = 0f;
            float turnAmount = -1f;
            if (action == MeleeBufferedAction.Kick)
            {
                float seconds = (progress < 0f ? KickAnimationProgress : progress) * kick.length;
                amount = seconds < .32f ? Mathf.SmoothStep(0f, 1f, (seconds - .08f) / .24f) :
                    1f - Mathf.SmoothStep(0f, 1f, (seconds - .43f) / .52f);
            }
            else if (action == MeleeBufferedAction.Shove)
                amount = CombatSupportGrip.ShoveReach(State.ShoveElapsed,
                    State.Settings.ShoveContactSeconds, State.Settings.ShoveDurationSeconds);
            else if (action == MeleeBufferedAction.Attack && !State.IsCharging)
            {
                float seconds = (progress < 0f ? State.AttackProgress : progress) * Current.Attack.length;
                float end = State.Settings.WindupSeconds + State.Settings.ActiveSeconds;
                amount = seconds < end ? Mathf.SmoothStep(0f, 1f, seconds / State.Settings.WindupSeconds) :
                    1f - Mathf.SmoothStep(0f, 1f, (seconds - end) / State.Settings.AnimationRecoverySeconds);
                // The body gathers its reach in preparation, but turns with
                // the actual arc rather than finishing its turn before release.
                turnAmount = seconds < end ? Mathf.SmoothStep(0f, 1f,
                    (seconds - State.Settings.WindupSeconds) / State.Settings.ActiveSeconds) : amount;
            }
            bodyMotion?.SetAttackReachPose(action, attackReach, amount, turnAmount < 0f ? amount : turnAmount,
                KickStrikingSide, State.Swing);
        }

        private void ResetAttackReach()
        { reachSequence = -1; reachAction = MeleeBufferedAction.None; attackReach = 0f; }
    }
}
