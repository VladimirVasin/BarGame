using UnityEngine;

namespace BarPromenade
{
    // Applied after the pooled resident's own idle and grounded soles.
    internal sealed class CityRailRestPose
    {
        private readonly CityPedestrianAssetRegistry body;
        private readonly CityRailLean lean;
        private readonly bool[] previousSkinUpdates;
        private readonly NpcHandPose hands;
        private CityPedestrianHandPropRegistry cigarette;
        private HomeBalconySmokingExhaleEffect exhale;
        private Transform mouth;
        private Vector3 filterInHand, cigaretteAxis;
        private CityRailRestSpot spot;
        private float time, period, breathePhase;
        private bool animatorWasEnabled;

        internal bool Bound { get; private set; }
        internal bool Smoking { get; private set; }

        internal CityRailRestPose(CityPedestrianAssetRegistry body)
        {
            this.body = body;
            previousSkinUpdates = new bool[body.Renderers.Count];
            hands = body.GetComponentInChildren<NpcHandPose>(true);
            lean = new CityRailLean(body, hands);
        }

        internal void Bind(CityRailRestSpot rest, bool smoking, float phase)
        {
            // A newly enabled Animator can still write its default upright
            // pose after the spawn tick. The rail owns these bones for the
            // whole visit; the walking graph resumes when the pool reuses it.
            animatorWasEnabled = body.Animator.enabled;
            body.Animator.enabled = false;
            spot = rest;
            period = Mathf.Lerp(17f, 22f, phase);
            time = period * phase;
            breathePhase = phase * Mathf.PI * 2f;
            if (smoking)
            {
                if (cigarette == null) PrepareSmoking();
                cigarette.gameObject.SetActive(true);
                exhale.gameObject.SetActive(true);
                // Street wardrobes share a neutral-to-curled hand shape.
                hands.SetGrip(false, .24f);
            }
            // The manual pose can arrive after an earlier camera or shadow
            // pass has cached this pooled body's upright skinning matrices.
            for (int i = 0; i < body.Renderers.Count; i++)
                if (body.Renderers[i] is SkinnedMeshRenderer skin)
                {
                    previousSkinUpdates[i] = skin.forceMatrixRecalculationPerRender;
                    skin.forceMatrixRecalculationPerRender = true;
                }
            Bound = true;
            Smoking = smoking;
        }

        internal CityPedestrianHandPropRegistry Cigarette => cigarette;
        internal HomeBalconySmokingExhaleEffect Exhale => exhale;

        internal void PrepareSmoking()
        {
            mouth = CityPedestrianHandProps.FindSocket(body.ModelRoot, CityPedestrianHandProps.MouthSocketName);
            Transform socket = CityPedestrianHandProps.FindSocket(body.ModelRoot, CityPedestrianHandPropId.Cigarette);
            cigarette = CityPedestrianHandProps.Attach(socket, CityPedestrianHandPropId.Cigarette, null, 0);
            Transform hand = lean.SmokingHand;
            var paper = cigarette.FindRenderer("ACC_Cigarette").GetComponent<MeshFilter>();
            Bounds bounds = paper.sharedMesh.bounds;
            int axis = bounds.size.x > bounds.size.y ? 0 : 1;
            if (bounds.size.z > bounds.size[axis]) axis = 2;
            Vector3 end = Vector3.zero;
            end[axis] = bounds.extents[axis];
            Vector3 a = paper.transform.TransformPoint(bounds.center - end);
            Vector3 b = paper.transform.TransformPoint(bounds.center + end);
            Vector3 ember = cigarette.FindRenderer("ACC_CigaretteEmber").bounds.center;
            if ((a - ember).sqrMagnitude < (b - ember).sqrMagnitude) (a, b) = (b, a);
            filterInHand = hand.InverseTransformPoint(a);
            cigaretteAxis = hand.InverseTransformDirection((b - a).normalized);
            var host = new GameObject("Rail smoker exhale");
            host.transform.SetParent(body.transform, false);
            exhale = host.AddComponent<HomeBalconySmokingExhaleEffect>();
            exhale.Initialize(mouth, CityBalconySmokerPresentation.CreateAnimationDefinition());
            exhale.EnableManualBurstMode();
        }

        internal void Apply(float delta)
        {
            float previous = time;
            time += delta;
            float cycle = Mathf.Repeat(time, period);
            lean.Apply(spot, Smoking, time, breathePhase);
            if (!Smoking) return;

            float lift = Mathf.SmoothStep(0f, 1f, (cycle - 1f) / 1.35f) *
                (1f - Mathf.SmoothStep(0f, 1f, (cycle - 3.8f) / 1.25f));
            LiftCigarette(lift);
            if (Mathf.FloorToInt((time - 5.2f) / period) > Mathf.FloorToInt((previous - 5.2f) / period))
                exhale.EmitManualBurst();
        }

        internal void ApplyBalcony(CityRailRestSpot rest, float elapsed, float lift, float leanWeight)
        {
            RestoreBones();
            lean.Apply(rest, true, elapsed, breathePhase);
            LiftCigarette(lift);
            lean.BlendFromStanding(leanWeight);
        }

        private void LiftCigarette(float lift)
        {
            if (lift > 0f)
            {
                Transform hand = lean.SmokingHand;
                Vector3 outward = mouth.up;
                Quaternion rotation = Quaternion.FromToRotation(hand.rotation * cigaretteAxis, outward) * hand.rotation;
                Vector3 offset = rotation * (Quaternion.Inverse(hand.rotation) *
                    (hand.TransformPoint(filterInHand) - hand.position));
                lean.RaiseSmokingHand(mouth.position + outward * .003f - offset, rotation, lift);
            }

        }

        internal void RestoreBones() => lean.RestoreBones();

        internal void Release()
        {
            if (!Bound) return;
            RestoreBones();
            if (Smoking)
            {
                hands.SetGrip(false, 0f);
                cigarette.gameObject.SetActive(false);
                exhale.gameObject.SetActive(false);
            }
            for (int i = 0; i < body.Renderers.Count; i++)
                if (body.Renderers[i] is SkinnedMeshRenderer skin && skin != null)
                    skin.forceMatrixRecalculationPerRender = previousSkinUpdates[i];
            body.Animator.enabled = animatorWasEnabled;
            Bound = Smoking = false;
        }
    }
}
