using System;

namespace BarPromenade
{
    /// <summary>Calendar-sampled ordinary dog activity. No Unity state, random stream or per-frame allocation.</summary>
    public static class CityDogActivity
    {
        public readonly struct Sample
        {
            public readonly float X, Z, HeadingDegrees, Speed, Distance, Sniff, Sit, Play;
            public Sample(float x, float z, float heading, float speed, float distance, float sniff, float sit, float play)
            { X = x; Z = z; HeadingDegrees = heading; Speed = speed; Distance = distance; Sniff = sniff; Sit = sit; Play = play; }
        }

        public static Sample Evaluate(int dog, double seconds)
        {
            if (dog < 0 || dog > 2) throw new ArgumentOutOfRangeException(nameof(dog));
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            double cycle = dog == 0 ? 47 : dog == 1 ? 59 : 73;
            double clock = seconds + dog * 11;
            float t = (float)(clock % cycle);
            float a = dog == 0 ? .65f : dog == 1 ? 1.40f : 2.05f;
            float b = dog == 0 ? 1.05f : dog == 1 ? 1.85f : 2.70f;
            float start = dog == 2 ? 8 : 3;
            float end = dog == 2 ? 36 : 19;
            float playStart = 23, playEnd = dog == 0 ? 31 : 34;
            float u = Smooth((t - start) / (end - start));
            float rate = SmoothRate((t - start) / (end - start)) / (end - start);
            float loopsPerCycle = dog == 2 ? 1 : 2;
            float play = 0;
            if (dog != 2 && t >= playStart)
            {
                u = 1 + Smooth((t - playStart) / (playEnd - playStart));
                rate = SmoothRate((t - playStart) / (playEnd - playStart)) / (playEnd - playStart);
                play = rate > 0 ? 1 : 0;
            }
            double theta = dog * 2.13 + u * Math.PI * 2;
            float sn = (float)Math.Sin(theta), cs = (float)Math.Cos(theta);
            float dx = -a * sn, dz = b * cs;
            float speed = (float)Math.Sqrt(dx * dx + dz * dz) * rate * (float)(2 * Math.PI);
            float perimeter = (float)(Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b))));
            float sitStart = dog == 2 ? 41 : dog == 0 ? 35 : 40;
            float sit = Smooth((t - sitStart) / 2) * (1 - Smooth((t - (float)cycle + 4) / 3));
            float sniff = speed < .03f ? (1 - sit) * (.65f + .25f * (float)Math.Sin(t * .7 + dog)) : 0;
            return new Sample(-.45f + a * cs, b * sn,
                (float)(Math.Atan2(dx, dz) * 180 / Math.PI), speed,
                (float)(Math.Floor(clock / cycle) * loopsPerCycle + u) * perimeter, sniff, sit, play);
        }

        private static float Smooth(float v) { v = Math.Max(0, Math.Min(1, v)); return v * v * (3 - 2 * v); }
        private static float SmoothRate(float v) => v <= 0 || v >= 1 ? 0 : 6 * v * (1 - v);
    }
}
