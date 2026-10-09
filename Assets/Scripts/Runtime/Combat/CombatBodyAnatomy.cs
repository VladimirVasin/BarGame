using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared mapping from damage regions to the unchanged production rig.</summary>
    public static class CombatBodyAnatomy
    {
        internal static BodyDamageRegion Parent(BodyDamageRegion region) => region switch
        {
            BodyDamageRegion.Head => BodyDamageRegion.Neck, BodyDamageRegion.Neck => BodyDamageRegion.Chest,
            BodyDamageRegion.Chest => BodyDamageRegion.Abdomen, BodyDamageRegion.Abdomen => BodyDamageRegion.Pelvis,
            BodyDamageRegion.LeftUpperArm or BodyDamageRegion.RightUpperArm => BodyDamageRegion.Chest,
            BodyDamageRegion.LeftForearm => BodyDamageRegion.LeftUpperArm, BodyDamageRegion.LeftHand => BodyDamageRegion.LeftForearm,
            BodyDamageRegion.RightForearm => BodyDamageRegion.RightUpperArm, BodyDamageRegion.RightHand => BodyDamageRegion.RightForearm,
            BodyDamageRegion.LeftThigh or BodyDamageRegion.RightThigh => BodyDamageRegion.Pelvis,
            BodyDamageRegion.LeftShin => BodyDamageRegion.LeftThigh, BodyDamageRegion.LeftFoot => BodyDamageRegion.LeftShin,
            BodyDamageRegion.RightShin => BodyDamageRegion.RightThigh, BodyDamageRegion.RightFoot => BodyDamageRegion.RightShin,
            _ => BodyDamageRegion.Pelvis
        };

        public static BodyDamageRegion ToRegion(Player3DAnatomicalPart part) => part switch
        {
            Player3DAnatomicalPart.Head => BodyDamageRegion.Head,
            Player3DAnatomicalPart.Neck => BodyDamageRegion.Neck,
            Player3DAnatomicalPart.Torso => BodyDamageRegion.Chest,
            Player3DAnatomicalPart.LowerTorso => BodyDamageRegion.Abdomen,
            Player3DAnatomicalPart.Pelvis => BodyDamageRegion.Pelvis,
            Player3DAnatomicalPart.LeftUpperArm => BodyDamageRegion.LeftUpperArm,
            Player3DAnatomicalPart.LeftForearm => BodyDamageRegion.LeftForearm,
            Player3DAnatomicalPart.LeftHand => BodyDamageRegion.LeftHand,
            Player3DAnatomicalPart.RightUpperArm => BodyDamageRegion.RightUpperArm,
            Player3DAnatomicalPart.RightForearm => BodyDamageRegion.RightForearm,
            Player3DAnatomicalPart.RightHand => BodyDamageRegion.RightHand,
            Player3DAnatomicalPart.LeftThigh => BodyDamageRegion.LeftThigh,
            Player3DAnatomicalPart.LeftShin => BodyDamageRegion.LeftShin,
            Player3DAnatomicalPart.LeftFoot => BodyDamageRegion.LeftFoot,
            Player3DAnatomicalPart.RightThigh => BodyDamageRegion.RightThigh,
            Player3DAnatomicalPart.RightShin => BodyDamageRegion.RightShin,
            Player3DAnatomicalPart.RightFoot => BodyDamageRegion.RightFoot,
            _ => BodyDamageRegion.Chest
        };

        public static Player3DAnatomicalPart ToPart(BodyDamageRegion region) => region switch
        {
            BodyDamageRegion.Head => Player3DAnatomicalPart.Head,
            BodyDamageRegion.Neck => Player3DAnatomicalPart.Neck,
            BodyDamageRegion.Chest => Player3DAnatomicalPart.Torso,
            BodyDamageRegion.Abdomen => Player3DAnatomicalPart.LowerTorso,
            BodyDamageRegion.Pelvis => Player3DAnatomicalPart.Pelvis,
            BodyDamageRegion.LeftUpperArm => Player3DAnatomicalPart.LeftUpperArm,
            BodyDamageRegion.LeftForearm => Player3DAnatomicalPart.LeftForearm,
            BodyDamageRegion.LeftHand => Player3DAnatomicalPart.LeftHand,
            BodyDamageRegion.RightUpperArm => Player3DAnatomicalPart.RightUpperArm,
            BodyDamageRegion.RightForearm => Player3DAnatomicalPart.RightForearm,
            BodyDamageRegion.RightHand => Player3DAnatomicalPart.RightHand,
            BodyDamageRegion.LeftThigh => Player3DAnatomicalPart.LeftThigh,
            BodyDamageRegion.LeftShin => Player3DAnatomicalPart.LeftShin,
            BodyDamageRegion.LeftFoot => Player3DAnatomicalPart.LeftFoot,
            BodyDamageRegion.RightThigh => Player3DAnatomicalPart.RightThigh,
            BodyDamageRegion.RightShin => Player3DAnatomicalPart.RightShin,
            BodyDamageRegion.RightFoot => Player3DAnatomicalPart.RightFoot,
            _ => Player3DAnatomicalPart.Torso
        };

        public static string BoneName(BodyDamageRegion region) => region switch
        {
            BodyDamageRegion.Head => "head", BodyDamageRegion.Neck => "neck",
            BodyDamageRegion.Chest => "chest", BodyDamageRegion.Abdomen => "spine",
            BodyDamageRegion.Pelvis => "pelvis",
            BodyDamageRegion.LeftUpperArm => "upper_arm.L", BodyDamageRegion.LeftForearm => "forearm.L",
            BodyDamageRegion.LeftHand => "hand.L", BodyDamageRegion.RightUpperArm => "upper_arm.R",
            BodyDamageRegion.RightForearm => "forearm.R", BodyDamageRegion.RightHand => "hand.R",
            BodyDamageRegion.LeftThigh => "thigh.L", BodyDamageRegion.LeftShin => "shin.L",
            BodyDamageRegion.LeftFoot => "foot.L", BodyDamageRegion.RightThigh => "thigh.R",
            BodyDamageRegion.RightShin => "shin.R", BodyDamageRegion.RightFoot => "foot.R", _ => "chest"
        };

        internal static MeleeBodyRegion CombatRegion(BodyDamageRegion region) => region switch
        {
            BodyDamageRegion.Head => MeleeBodyRegion.Head,
            BodyDamageRegion.LeftUpperArm or BodyDamageRegion.LeftForearm or BodyDamageRegion.LeftHand => MeleeBodyRegion.LeftArm,
            BodyDamageRegion.RightUpperArm or BodyDamageRegion.RightForearm or BodyDamageRegion.RightHand => MeleeBodyRegion.RightArm,
            BodyDamageRegion.LeftThigh or BodyDamageRegion.LeftShin or BodyDamageRegion.LeftFoot => MeleeBodyRegion.LeftLeg,
            BodyDamageRegion.RightThigh or BodyDamageRegion.RightShin or BodyDamageRegion.RightFoot => MeleeBodyRegion.RightLeg,
            _ => MeleeBodyRegion.Torso
        };
    }
}
