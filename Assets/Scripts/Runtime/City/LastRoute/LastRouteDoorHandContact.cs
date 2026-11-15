using UnityEngine;

namespace BarPromenade
{
    /// <summary>Keep a reaching palm with its actual pull while a car door swings.</summary>
    internal sealed class LastRouteDoorHandContact
    {
        private readonly Transform upper, forearm, hand, grip;
        private readonly SeatedArmHandAttachment attachment;

        internal LastRouteDoorHandContact(Transform upper, Transform forearm, Transform hand, Transform grip)
        {
            this.upper = upper;
            this.forearm = forearm;
            this.hand = hand;
            this.grip = grip;
            attachment = new SeatedArmHandAttachment(hand, grip);
        }

        internal static LastRouteDoorHandContact ForHero(Player3DAssetRegistry hero, bool left)
        {
            string side = left ? ".L" : ".R";
            Transform model = hero.ModelRoot;
            Transform upper = CityPedestrianHandProps.FindSocket(model, "upper_arm" + side);
            Transform lower = CityPedestrianHandProps.FindSocket(model, "forearm" + side);
            Transform hand = CityPedestrianHandProps.FindSocket(model, "hand" + side);
            Transform grip = left ? hero.Anchors.LeftGrip : hero.Anchors.RightGrip;
            return upper != null && lower != null && hand != null && grip != null
                ? new LastRouteDoorHandContact(upper, lower, hand, grip)
                : null;
        }

        internal void Apply(Transform target, float weight)
        {
            if (target == null || weight <= 0f) return;
            Vector3 goal = target.position - hand.rotation * attachment.SocketPositionInHand;
            float armLength = Vector3.Distance(upper.position, forearm.position) +
                Vector3.Distance(forearm.position, hand.position);
            // Release as the open leaf leaves reach. The sampled pose owns
            // the hand again before the next authored elbow transition.
            weight *= Mathf.InverseLerp(armLength + .055f, armLength - .016f, Vector3.Distance(upper.position, goal));
            if (weight <= 0f) return;
            SeatedArmIk.SolveTwoBone(upper, forearm, hand,
                Vector3.Lerp(hand.position, goal, weight), hand.rotation, forearm.position - Vector3.up * .07f);
        }

        internal static float Hold(float t, float approach, float contact, float release, float clear)
        {
            float weight = Mathf.Min(Mathf.InverseLerp(approach, contact, t), 1f - Mathf.InverseLerp(release, clear, t));
            return Mathf.SmoothStep(0f, 1f, weight);
        }
    }
}
