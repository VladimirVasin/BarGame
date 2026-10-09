using UnityEngine;

namespace BarPromenade
{
    public enum CombatImpactKind { Weapon, Kick, Shove, Projectile }
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
        public Player3DAnatomicalPart Part { get; }
        public Vector3 LocalPoint { get; }
        public Vector3 LocalDirection { get; }
        public float WeaponSpeed { get; }
        /// <summary>World-space momentum (N s), separate from anatomical HP damage.</summary>
        public Vector3 Impulse { get; }
        public CombatImpactKind Kind { get; }
        public int PelletIndex { get; }
        public bool IsPellet => PelletIndex >= 0;
        public bool PrimaryResponse { get; }
        public bool HeadFeedback { get; }
        public float WoundDamage { get; }
        public float HeadTrauma { get; }
        public BodyDamageRegion? BodyRegion { get; }
        public int BodyPatch { get; }
        public bool DetachedPart { get; }
        public bool IsCritical => (Kind == CombatImpactKind.Weapon || Kind == CombatImpactKind.Projectile) && Damage > 0f && Location.IsCritical;
        public bool IsFinisher => Kind == CombatImpactKind.Weapon && Damage > 0f && Location.IsFinisher;

        public CombatImpact(CombatActor source, CombatActor target, int sequence,
            Vector3 point, Vector3 normal, Vector3 direction, float healthBefore,
            float healthAfter, MeleeHitResult result, MeleeHitLocation location = default, float attackPower = 0f,
            Player3DAnatomicalPart part = Player3DAnatomicalPart.Torso, Vector3 localPoint = default,
            float weaponSpeed = 0f, Vector3 impulse = default, CombatImpactKind kind = CombatImpactKind.Weapon,
            Vector3 localDirection = default, int pelletIndex = -1, bool primaryResponse = true,
            bool headFeedback = true, float woundDamage = 0f, float headTrauma = 0f,
            BodyDamageRegion? bodyRegion = null, int bodyPatch = -1, bool detachedPart = false)
        {
            Source = source; Target = target; AttackSequence = sequence;
            Point = point; Normal = normal; Direction = direction;
            HealthBefore = healthBefore; HealthAfter = healthAfter; Result = result;
            Location = location;
            AttackPower = attackPower;
            Part = part; LocalPoint = localPoint; WeaponSpeed = weaponSpeed; Impulse = impulse;
            Kind = kind;
            LocalDirection = localDirection;
            PelletIndex = pelletIndex; PrimaryResponse = primaryResponse; HeadFeedback = headFeedback; WoundDamage = woundDamage;
            HeadTrauma = headTrauma;
            BodyRegion = bodyRegion; BodyPatch = bodyPatch; DetachedPart = detachedPart;
        }
    }
}
