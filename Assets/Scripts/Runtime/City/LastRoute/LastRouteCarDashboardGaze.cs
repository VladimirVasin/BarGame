using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Which thing on the dash a ray from the passenger's eye lands on.
    ///
    /// Not the interactor's overlap sphere, and not for want of trying:
    /// while he is seated the hero's CAPSULE is still standing at the door
    /// dock outside the car - only his drawn pelvis is bound to the seat -
    /// so the dash is two metres beyond the sphere's reach and a collider
    /// on the radio would never be found. The seat is the one interactable
    /// the sphere does find, and it asks this what he is looking at.
    ///
    /// Pure, on world bounds, so it can be proved against the seat's own
    /// camera plan in an EditMode test. The radio is split down the middle
    /// into its two knobs; the lid's bounds move with the lid, which is
    /// right - an open lid is looked at where it hangs.
    /// </summary>
    public static class LastRouteCarDashboardGaze
    {
        /// <summary>How far outside a part's drawn box a look still counts.
        /// A knob is a centimetre and a half across; nobody aims that well
        /// with a mouse.</summary>
        public const float PickTolerance = 0.03f;

        public static LastRouteCarDashboardTarget Resolve(
            Ray ray,
            Bounds radio,
            Vector3 radioCentre,
            Vector3 towardsDriver,
            Bounds lid)
        {
            if (ray.direction.sqrMagnitude < 0.000001f)
            {
                return LastRouteCarDashboardTarget.None;
            }

            Bounds radioPick = radio;
            radioPick.Expand(PickTolerance * 2f);
            Bounds lidPick = lid;
            lidPick.Expand(PickTolerance * 2f);

            bool hitsRadio = radioPick.IntersectRay(ray, out float radioDistance);
            bool hitsLid = lidPick.IntersectRay(ray, out float lidDistance);
            if (!hitsRadio && !hitsLid)
            {
                return LastRouteCarDashboardTarget.None;
            }

            if (hitsLid && (!hitsRadio || lidDistance < radioDistance))
            {
                return LastRouteCarDashboardTarget.Glovebox;
            }

            // The power knob is on the driver's side of the dial, the tuning
            // knob on the passenger's - the way a radio is laid out.
            Vector3 hit = ray.GetPoint(radioDistance);
            return Vector3.Dot(hit - radioCentre, towardsDriver) >= 0f
                ? LastRouteCarDashboardTarget.RadioPower
                : LastRouteCarDashboardTarget.RadioTuning;
        }
    }
}
