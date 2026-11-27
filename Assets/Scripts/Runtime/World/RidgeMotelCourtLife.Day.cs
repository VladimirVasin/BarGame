using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class RidgeMotelCourtLife
    {
        private Transform cart, bundle, hatch;
        private Quaternion hatchRest;
        private readonly Transform[] luggage = new Transform[2];
        private readonly Pose[] boot = new Pose[2];
        private static readonly double[] LuggageTrips = { 12*60 + 22, 12*60 + 38 };
        private Vector3 towelsAtRest;
        private Quaternion towelRotation;
        private VillageResidentPresentation housekeeper;
        private Vector3[] linenRoute;
        private float[] routeLength;
        private float totalMetres;

        private void InitializeLinen(Transform model)
        {
            hatch = model.Find("WagonHatch");
            hatchRest = hatch.rotation;
            luggage[0] = model.Find("TravelCase"); luggage[1] = model.Find("TravelCoat");
            for (int i = 0; i < luggage.Length; i++) boot[i] = new Pose(luggage[i].position, luggage[i].rotation);
            cart = model.Find("LinenCart");
            bundle = model.Find("LinenBundle");
            towelsAtRest = bundle.position;
            towelRotation = bundle.rotation;
            var solid = new GameObject("Linen cart body").AddComponent<BoxCollider>();
            solid.transform.SetParent(transform, false);
            solid.transform.position = cart.position;
            solid.size = new Vector3(.74f, .9f, .49f);
            solid.center = Vector3.up * .45f;
            linenRoute = new[]
            {
                new Vector3(-10.8f, 0, 2.03f), new Vector3(-10.8f, 0, .84f),
                new Vector3(-15.55f, 0, .84f), new Vector3(-15.55f, 0, 1.92f),
                new Vector3(-15.55f, 1.53f, 4.8f), new Vector3(-15.55f, 1.53f, 5.54f),
                new Vector3(-13.9f, 1.53f, 5.54f), new Vector3(-13.9f, 1.53f, 4.8f),
                new Vector3(-13.9f, 3.06f, 1.92f), new Vector3(-13.9f, 3.06f, .84f),
                new Vector3(-9.98f, 3.06f, .84f)
            };
            routeLength = new float[linenRoute.Length];
            for (int i = 0; i < linenRoute.Length; i++)
            {
                linenRoute[i] = transform.TransformPoint(linenRoute[i]);
                if (i > 0) totalMetres += Vector3.Distance(linenRoute[i - 1], linenRoute[i]);
                routeLength[i] = totalMetres;
            }
        }

        // The same attendant later brings the first two beers to the courtyard.
        private void Clerk(double minute, float dt)
        {
            Guest g = guests[0];
            PrepareGuest(0);
            ReleasePose(g);
            g.Body.gameObject.SetActive(true);
            float shift = RealSeconds(minute - 480d);
            float alongCounter = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(shift / 24f, 1f));
            Vector3 at = transform.TransformPoint(new Vector3(10.95f + alongCounter * .55f, .11f, 8.28f));
            g.Body.transform.SetPositionAndRotation(at, Quaternion.LookRotation(transform.forward));
            g.Body.SetAttentionFocus(at + transform.forward * .52f + Vector3.up * 1.03f);
            g.Body.Advance(dt, false);
        }

        private bool Traveller(double minute, float dt, bool near, bool seek)
        {
            bool present = false;
            float lid = 0f;
            for (int i = 0; i < luggage.Length; i++)
            {
                float t = RealSeconds(minute - LuggageTrips[i]);
                bool inYard = near && t >= 0 && t < 33f;
                luggage[i].gameObject.SetActive(t < 0 || inYard);
                if (!inYard)
                {
                    if (t < 0) luggage[i].SetPositionAndRotation(boot[i].position, boot[i].rotation);
                    continue;
                }
                present = true;
                PrepareGuest(1);
                var g = guests[1];
                ReleasePose(g);
                g.Body.gameObject.SetActive(true);
                Vector3 tail = transform.TransformPoint(new Vector3(3.50f, 0, i == 0 ? 6.82f : 7.30f));
                Vector3 gate = transform.TransformPoint(new Vector3(-5.4f, 0, 17.05f));
                Vector3 corner = transform.TransformPoint(new Vector3(2.4f, 0, 14.7f));
                Vector3 aisle = new(corner.x, tail.y, tail.z);
                float path = Mathf.Clamp01(t / 13f) * (1f - Mathf.InverseLerp(20f, 33f, t));
                Route(gate, corner, aisle, tail, path, out Vector3 feet, out Vector3 forward);
                if (t > 20f) forward = -forward;
                Vector3 displacement = feet - g.Body.transform.position;
                g.Body.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(forward));
                g.Body.AdvanceTravelled(dt, !seek && displacement.sqrMagnitude > .000001f,
                    seek ? Vector3.zero : displacement);
                lid = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(13f,14.5f,t)) *
                    (1f - Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(18.5f,20f,t)));
                float pick = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(15.6f,17.5f,t));
                Vector3 offset = Vector3.up * (i == 0 ? .39f : .06f);
                Vector3 carry = feet + g.Body.transform.right * .25f + forward * .12f + Vector3.up * .78f;
                Vector3 target = Vector3.Lerp(boot[i].position + offset, carry, pick);
                Vector3 grip = g.Hands.ReachFor(target, Mathf.InverseLerp(14.2f,15.6f,t));
                luggage[i].SetPositionAndRotation(Vector3.Lerp(boot[i].position, grip - offset, pick),
                    Quaternion.Slerp(boot[i].rotation, g.Body.transform.rotation,t > 20f ? pick : 0f));
                if (t >= 13f && t < 15f || t > 18.3f && t <= 20f)
                    g.Hands.ReachFor(tail + Vector3.right * .34f + Vector3.up * (1.03f + lid * .71f), .85f, true);
            }
            hatch.rotation = Quaternion.AngleAxis(-87f * lid, transform.forward) * hatchRest;
            return present;
        }

        private void SampleDay(double minute, bool near)
        {
            bool working = near && minute >= 8 * 60 && minute < 17 * 60;
            if (!working)
            {
                if (housekeeper && housekeeper.gameObject.activeSelf) housekeeper.gameObject.SetActive(false);
                bundle.SetPositionAndRotation(towelsAtRest, towelRotation);
                return;
            }
            if (!housekeeper)
            {
                housekeeper = DefaultNpcFactory.CreateForCharacter(transform, DefaultNpcPopulation.MotelHousekeeper);
                CityPortCrew.AlignWorkerModelWithPlacement(housekeeper);
            }
            housekeeper.gameObject.SetActive(true);
            float phase = RealSeconds(minute - 480d) % 118f;
            float path = Mathf.InverseLerp(11f, 35f, phase) - Mathf.InverseLerp(74f, 98f, phase);
            bool walking = phase > 11f && phase < 35f || phase > 74f && phase < 98f;
            float metres = totalMetres * path;
            int segment = 1;
            while (segment + 1 < linenRoute.Length && routeLength[segment] < metres) segment++;
            Vector3 a = linenRoute[segment - 1], b = linenRoute[segment];
            Vector3 feet = Vector3.Lerp(a, b, Mathf.InverseLerp(routeLength[segment - 1], routeLength[segment], metres));
            Vector3 direction = walking ? Vector3.ProjectOnPlane((phase < 35f ? 1f : -1f) * (b - a), Vector3.up)
                : path > .5f ? Vector3.back : Vector3.forward;
            var facing = Quaternion.LookRotation(direction);
            housekeeper.transform.SetPositionAndRotation(feet, facing);
            float hold = Mathf.InverseLerp(8f, 10f, phase) * (1f - Mathf.InverseLerp(36f, 38f, phase)) +
                Mathf.InverseLerp(71f, 73f, phase) * (1f - Mathf.InverseLerp(99f, 101f, phase));
            if (walking) housekeeper.ApplyLocomotion(totalMetres / 24f, true, phase);
            else housekeeper.Apply(hold > .01f ? VillageResidentAction.Carry : VillageResidentAction.StationWork, phase);
            Vector3 ledge = transform.TransformPoint(new Vector3(-9.98f, 3.06f + .945f, .045f));
            Vector3 supported = path > .5f ? ledge : towelsAtRest;
            Vector3 chest = feet + facing * new Vector3(0, 1.19f, .29f);
            Vector3 load = Vector3.Lerp(supported, chest, Mathf.SmoothStep(0f, 1f, hold));
            bundle.SetPositionAndRotation(load, Quaternion.Slerp(towelRotation, facing, hold));
            float reach = Mathf.Max(hold, .65f * (1f - Mathf.Clamp01(Mathf.Min(Mathf.Abs(phase-8f), Mathf.Abs(phase-71f)))));
            if (reach > 0f)
            {
                Vector3 edge = facing * new Vector3(.17f, .036f, 0);
                housekeeper.ApplyHandContacts(load + edge, load - edge, reach);
            }
        }
    }
}
