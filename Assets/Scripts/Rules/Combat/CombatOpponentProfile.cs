using System;

namespace BarPromenade
{
    public enum CombatOpponentStyle { Cautious, Pressuring, Patient }

    /// <summary>Decision preferences only. Every style shares the duel's body, speed and reaction clock.</summary>
    public sealed class CombatOpponentProfile
    {
        private static readonly CombatOpponentProfile Cautious = new CombatOpponentProfile(
            CombatOpponentStyle.Cautious);
        private static readonly CombatOpponentProfile Pressuring = new CombatOpponentProfile(
            CombatOpponentStyle.Pressuring, hitChainPercent: 85, guardTellPercent: 40,
            sideStepTellPercent: 25, backStepTellPercent: 15, staggerCoverPercent: 40,
            blockedGuardPercent: 35, blockedBackStepPercent: 15, missCoverPercent: 35,
            probeDelayMinimum: .10f, probeDelayMaximum: .25f,
            pressDelayMinimum: .10f, pressDelayMaximum: .25f,
            probeMissMinimum: .10f, probeMissMaximum: .25f,
            pressMissMinimum: .10f, pressMissMaximum: .25f,
            probeFeintPercent: 12, pressFeintPercent: 20, chargeInterceptPercent: 85,
            probeLightPercent: 60, pressLightPercent: 45, kickBackStepPercent: 35);
        private static readonly CombatOpponentProfile Patient = new CombatOpponentProfile(
            CombatOpponentStyle.Patient, hitChainPercent: 40, guardTellPercent: 65,
            sideStepTellPercent: 15, backStepTellPercent: 15, staggerCoverPercent: 75,
            blockedGuardPercent: 60, blockedBackStepPercent: 25, missCoverPercent: 75,
            probeDelayMinimum: .35f, probeDelayMaximum: .65f,
            pressDelayMinimum: .25f, pressDelayMaximum: .50f,
            probeMissMinimum: .35f, probeMissMaximum: .65f,
            pressMissMinimum: .35f, pressMissMaximum: .65f,
            probeFeintPercent: 5, pressFeintPercent: 10, chargeInterceptPercent: 40,
            probeLightPercent: 85, pressLightPercent: 70, kickBackStepPercent: 75,
            idleLimitSeconds: 1f);

        private CombatOpponentProfile(CombatOpponentStyle style, int hitChainPercent = 75,
            int guardTellPercent = 60, int sideStepTellPercent = 15, int backStepTellPercent = 10,
            int staggerCoverPercent = 60, int blockedGuardPercent = 50, int blockedBackStepPercent = 20,
            int missCoverPercent = 60, float probeDelayMinimum = .18f, float probeDelayMaximum = .42f,
            float pressDelayMinimum = .10f, float pressDelayMaximum = .30f,
            float probeMissMinimum = .18f, float probeMissMaximum = .42f,
            float pressMissMinimum = .10f, float pressMissMaximum = .30f,
            int probeFeintPercent = 8, int pressFeintPercent = 15, int chargeInterceptPercent = 70,
            int probeLightPercent = 75, int pressLightPercent = 55, int kickBackStepPercent = 60,
            float idleLimitSeconds = float.PositiveInfinity)
        {
            Style = style;
            HitChainPercent = hitChainPercent;
            GuardTellPercent = guardTellPercent;
            SideStepTellPercent = sideStepTellPercent;
            BackStepTellPercent = backStepTellPercent;
            StaggerCoverPercent = staggerCoverPercent;
            BlockedGuardPercent = blockedGuardPercent;
            BlockedBackStepPercent = blockedBackStepPercent;
            MissCoverPercent = missCoverPercent;
            ProbeDelayMinimum = probeDelayMinimum; ProbeDelayMaximum = probeDelayMaximum;
            PressDelayMinimum = pressDelayMinimum; PressDelayMaximum = pressDelayMaximum;
            ProbeMissMinimum = probeMissMinimum; ProbeMissMaximum = probeMissMaximum;
            PressMissMinimum = pressMissMinimum; PressMissMaximum = pressMissMaximum;
            ProbeFeintPercent = probeFeintPercent; PressFeintPercent = pressFeintPercent;
            ChargeInterceptPercent = chargeInterceptPercent;
            ProbeLightPercent = probeLightPercent; PressLightPercent = pressLightPercent;
            KickBackStepPercent = kickBackStepPercent;
            IdleLimitSeconds = idleLimitSeconds;
        }

        public CombatOpponentStyle Style { get; }
        public int HitChainPercent { get; }
        public int GuardTellPercent { get; }
        public int SideStepTellPercent { get; }
        public int BackStepTellPercent { get; }
        public int StaggerCoverPercent { get; }
        public int BlockedGuardPercent { get; }
        public int BlockedBackStepPercent { get; }
        public int MissCoverPercent { get; }
        public float ProbeDelayMinimum { get; }
        public float ProbeDelayMaximum { get; }
        public float PressDelayMinimum { get; }
        public float PressDelayMaximum { get; }
        public float ProbeMissMinimum { get; }
        public float ProbeMissMaximum { get; }
        public float PressMissMinimum { get; }
        public float PressMissMaximum { get; }
        public int ProbeFeintPercent { get; }
        public int PressFeintPercent { get; }
        public int ChargeInterceptPercent { get; }
        public int ProbeLightPercent { get; }
        public int PressLightPercent { get; }
        public int KickBackStepPercent { get; }
        public float IdleLimitSeconds { get; }

        public static CombatOpponentProfile For(CombatOpponentStyle style) => style switch
        {
            CombatOpponentStyle.Cautious => Cautious,
            CombatOpponentStyle.Pressuring => Pressuring,
            CombatOpponentStyle.Patient => Patient,
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown opponent style.")
        };
    }
}
