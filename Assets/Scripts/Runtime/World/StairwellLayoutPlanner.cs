using UnityEngine;

namespace BarPromenade
{
    public static class StairwellLayoutPlanner
    {
        public const int StepsPerFlight = 16;
        public const float StepRise = 1.6f / StepsPerFlight;
        public const float StepDepth = 0.24f;
        public const float FlightWidth = 1.55f;

        public static StairwellLayoutPlan Generate()
        {
            const float street = 0f;
            const float middle = 1.6f;
            const float apartment = 3.2f;
            const float leftFlightX = -1.45f;
            const float rightFlightX = 1.45f;
            const float flightStartZ = -3.04f;
            const float flightEndZ = 0.8f;

            Rect streetLobby =
                Rect.MinMaxRect(-4.05f, -4.50f, 4.05f, -3.04f);
            Rect lowerFlight =
                Rect.MinMaxRect(-2.225f, -3.76f, -0.675f, 1.52f);
            Rect middleLanding =
                Rect.MinMaxRect(-2.225f, 0.8f, 2.225f, 2.25f);
            Rect apartmentFlight =
                Rect.MinMaxRect(0.675f, -3.76f, 2.225f, 1.52f);
            Rect apartmentLanding =
                Rect.MinMaxRect(-2.40f, -4.40f, 4.05f, -3.04f);
            Rect upperFlight =
                Rect.MinMaxRect(-2.225f, -3.76f, -0.675f, 1.52f);

            var lower = new StairwellFlightPlan(
                "street-to-middle",
                new Vector2(leftFlightX, flightStartZ),
                Vector2.up,
                street,
                StepsPerFlight,
                StepRise,
                StepDepth,
                FlightWidth);
            var toApartment = new StairwellFlightPlan(
                "middle-to-apartment",
                new Vector2(rightFlightX, flightEndZ),
                Vector2.down,
                middle,
                StepsPerFlight,
                StepRise,
                StepDepth,
                FlightWidth);
            var upper = new StairwellFlightPlan(
                "blocked-upper-flight",
                new Vector2(leftFlightX, flightStartZ),
                Vector2.up,
                apartment,
                StepsPerFlight,
                StepRise,
                StepDepth,
                FlightWidth);

            var plan = new StairwellLayoutPlan(
                new Vector2(8.6f, 9.6f),
                6.25f,
                street,
                middle,
                apartment,
                streetLobby,
                lowerFlight,
                middleLanding,
                apartmentFlight,
                apartmentLanding,
                upperFlight,
                lower,
                toApartment,
                upper,
                new Vector3(-1.45f, 0.14f, -3.78f),
                new Vector3(3.18f, 3.34f, -3.55f),
                new Vector3(-1.45f, 1.02f, -4.44f),
                new Vector3(1.62f, 2.04f, 0.68f),
                new Vector3(3.82f, 4.22f, -3.20f),
                new Vector3(0.70f, 2.04f, 1.68f),
                new Bounds(
                    new Vector3(-1.45f, 4.20f, -0.46f),
                    new Vector3(1.72f, 2.0f, 0.88f)));
            StairwellLayoutValidator.ValidateOrThrow(plan);
            return plan;
        }
    }
}
