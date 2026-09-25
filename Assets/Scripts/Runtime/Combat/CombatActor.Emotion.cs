using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal float CombatFearAmount => bodyMotion?.FearAmount ?? 0f;

        private void UpdateCombatEmotion()
        {
            if (hero != null)
            {
                if (roundEnded || State.IsDefeated) hero.ReleaseContextualFacialExpression(this);
                else
                {
                    float blink = Mathf.Repeat(poseClock + 1f, 3.1f);
                    PlayerFacialExpression face = blink < .065f ? PlayerFacialExpression.ClosedBlink :
                        blink < .12f ? PlayerFacialExpression.HalfBlink :
                        State.IsAttacking || State.IsCharging || State.IsShoving || State.IsBlocking || (bodyMotion?.FlinchAmount ?? 0f) > .1f
                            ? PlayerFacialExpression.Tense : PlayerFacialExpression.Watchful;
                    hero.TrySetContextualFacialExpression(this, face);
                }
            }
            float threat = 0f;
            if (hero != null && contactTarget != null && !contactTarget.State.IsDefeated &&
                (contactTarget.State.Phase == MeleePhase.Windup || contactTarget.State.IsCharging))
            {
                Vector3 delta = contactTarget.transform.position - transform.position;
                float distance = delta.magnitude;
                if (distance < 3f && distance > .01f && Vector3.Dot(transform.forward, delta / distance) > .35f)
                {
                    Vector3 from = transform.position + Vector3.up * 1.35f;
                    Vector3 to = contactTarget.transform.position + Vector3.up * 1.35f;
                    bool obstructed = Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) &&
                        hit.collider.GetComponentInParent<CombatActor>() != contactTarget &&
                        hit.collider.GetComponentInParent<CombatActor>() != this;
                    if (!obstructed) threat = Mathf.Lerp(.35f, 1f, Mathf.Clamp01((3f - distance) / 1.6f));
                }
            }
            float effort = State.Phase == MeleePhase.Recovery ? 1f - State.PhaseProgress :
                State.IsAttacking || State.IsShoving ? 1f : State.IsCharging ? .35f + .65f * State.Charge01 : 0f;
            bodyMotion?.SetEmotionTargets(hero != null, State.Stamina / State.Settings.MaxStamina,
                threat, effort, !roundEnded && !State.IsDefeated);
        }
    }
}
