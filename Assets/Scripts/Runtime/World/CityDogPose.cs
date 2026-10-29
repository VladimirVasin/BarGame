using UnityEngine;

namespace BarPromenade
{
    /// <summary>Four planted two-bone limbs over the imported animal, measured in its metre-sized host.</summary>
    internal sealed class CityDogPose
    {
        private sealed class Leg
        {
            public Transform Upper, Lower, Paw;
            public Quaternion UpperRest, LowerRest, PawRest;
            public Vector3 Foot;
        }
        private readonly Transform host, torso, head, earL, earR, tailA, tailB;
        private readonly Vector3 torsoRest;
        private readonly Quaternion torsoRotation, headRest, earLRest, earRRest, tailARest, tailBRest;
        private readonly Leg[] legs = new Leg[4];
        private readonly float height, stride;
        private readonly Vector3 frontShoulderFromTorso;
        public float PawError { get; private set; }

        public CityDogPose(Transform host, Transform model)
        {
            this.host = host;
            Transform Find(string name) => CityDogParkAssetProvider.FindPart(model, name);
            torso = Find("MOVE_Torso"); head = Find("MOVE_Head");
            earL = Find("MOVE_EarL"); earR = Find("MOVE_EarR");
            tailA = Find("MOVE_Tail01"); tailB = Find("MOVE_Tail02");
            torsoRest = torso.localPosition; torsoRotation = torso.localRotation;
            headRest = head.localRotation; earLRest = earL.localRotation; earRRest = earR.localRotation;
            tailARest = tailA.localRotation; tailBRest = tailB.localRotation;
            string[] names = { "FL", "FR", "BL", "BR" };
            for (int i = 0; i < legs.Length; i++)
            {
                var leg = new Leg { Upper = Find("MOVE_Leg" + names[i] + "_Upper"),
                    Lower = Find("MOVE_Leg" + names[i] + "_Lower"), Paw = Find("MOVE_Leg" + names[i] + "_Paw") };
                leg.UpperRest = leg.Upper.localRotation; leg.LowerRest = leg.Lower.localRotation;
                leg.PawRest = Quaternion.Inverse(host.rotation) * leg.Paw.rotation;
                leg.Foot = host.InverseTransformPoint(leg.Paw.position);
                legs[i] = leg;
            }
            height = host.InverseTransformPoint(legs[0].Upper.position).y;
            stride = height * .57f;
            frontShoulderFromTorso = host.InverseTransformDirection(legs[0].Upper.position - torso.position);
        }

        public void Apply(double seconds, float distance, float speed, CityDogActivity.Sample activity)
        {
            float t = (float)(seconds % 3600);
            float moving = Mathf.Clamp01(speed / .2f);
            float sit = activity.Sit * (1 - moving);
            float sniff = activity.Sniff * (1 - moving);
            // Rotate at the hips and lower them by the exact rise of the front
            // shoulders: a sitting dog folds its rear legs, not its front contacts.
            float pitch = -29f * sit + 7f * sniff;
            Vector3 shoulderRotated = Quaternion.Euler(pitch, 0f, 0f) * frontShoulderFromTorso;
            float drop = shoulderRotated.y - frontShoulderFromTorso.y + height * .035f;
            Vector3 worldOffset = Vector3.down * drop + Vector3.up * Mathf.Sin(t * 1.9f) * height * .007f;
            torso.localPosition = torsoRest + torso.parent.InverseTransformVector(worldOffset);
            torso.localRotation = torsoRotation * Quaternion.Euler(pitch, 0f, Mathf.Sin(t * 2.1f) * moving * .8f);
            head.localRotation = headRest * Quaternion.Euler(24f * sniff - 9f * sit,
                Mathf.Sin(t * .57f + activity.X) * (moving > .1f ? 4f : 13f), 0f);
            earL.localRotation = earLRest * Quaternion.Euler(Mathf.Sin(t * 1.2f) * 5f, 0f, 2f * sniff);
            earR.localRotation = earRRest * Quaternion.Euler(Mathf.Sin(t * 1.37f + 1.8f) * 4f, 0f, -2f * sniff);
            float wag = Mathf.Sin(t * (activity.Play > 0 ? 6.8f : 3.1f)) * (activity.Play > 0 ? 24f : 11f);
            tailA.localRotation = tailARest * Quaternion.Euler(-10f * activity.Play + 15f * sit, wag, 0f);
            tailB.localRotation = tailBRest * Quaternion.Euler(0f, wag * .7f, 0f);
            float duty = activity.Play > .5f ? .56f : .68f;
            float period = Mathf.Max(.15f, stride / duty);
            float cycle = distance / period;
            PawError = 0;
            for (int i = 0; i < legs.Length; i++)
            {
                Leg leg = legs[i];
                leg.Upper.localRotation = leg.UpperRest; leg.Lower.localRotation = leg.LowerRest;
                float offset = activity.Play > .5f ? (i == 0 || i == 3 ? 0f : .5f) :
                    i == 0 ? 0f : i == 1 ? .5f : i == 2 ? .75f : .25f;
                float phase = Mathf.Repeat(cycle + offset, 1f);
                float step = phase < duty ? Mathf.Lerp(.5f, -.5f, phase / duty) :
                    Mathf.Lerp(-.5f, .5f, Mathf.SmoothStep(0f, 1f, (phase - duty) / (1 - duty)));
                float lift = phase < duty ? 0 : Mathf.Sin((phase - duty) / (1 - duty) * Mathf.PI) * height * .19f;
                Vector3 target = leg.Foot + new Vector3(0, lift * moving, step * stride * moving);
                if (i >= 2) target.z += height * .34f * sit;
                Vector3 worldTarget = host.TransformPoint(target);
                Vector3 hint = leg.Upper.position + host.forward * height * (i < 2 ? -.65f : 1.1f);
                LimbTwoBoneIk.Solve(leg.Upper, leg.Lower, leg.Paw, worldTarget,
                    host.rotation * leg.PawRest, hint, 1f, .999f, true);
                PawError = Mathf.Max(PawError, Vector3.Distance(leg.Paw.position, worldTarget));
            }
        }
    }
}
