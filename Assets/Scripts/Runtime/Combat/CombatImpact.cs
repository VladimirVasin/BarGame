using UnityEngine;

namespace BarPromenade
{
    /// <summary>One resolved contact. Presentation cannot change its damage or replay its strike.</summary>
    public readonly struct CombatImpact
    {
        public CombatActor Source { get; }
        public CombatActor Target { get; }
        public int AttackSequence { get; }
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public Vector3 Direction { get; }
        public float HealthBefore { get; }
        public float HealthAfter { get; }
        public float Damage => Mathf.Max(0f, HealthBefore - HealthAfter);
        public MeleeHitResult Result { get; }
        public MeleeHitLocation Location { get; }
        public float AttackPower { get; }
        public bool IsCritical => Damage > 0f && Location.IsCritical;
        public bool IsFinisher => Damage > 0f && Location.IsFinisher;

        public CombatImpact(CombatActor source, CombatActor target, int sequence,
            Vector3 point, Vector3 normal, Vector3 direction, float healthBefore,
            float healthAfter, MeleeHitResult result, MeleeHitLocation location = default, float attackPower = 0f)
        {
            Source = source; Target = target; AttackSequence = sequence;
            Point = point; Normal = normal; Direction = direction;
            HealthBefore = healthBefore; HealthAfter = healthAfter; Result = result;
            Location = location;
            AttackPower = attackPower;
        }
    }
}
