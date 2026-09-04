#ifndef BAR_PROMENADE_BEGOTTEN_FILM_INCLUDED
#define BAR_PROMENADE_BEGOTTEN_FILM_INCLUDED

// The stock of the Begotten print: hash, grain, dust, hairs and
// scratches. Everything is a function of the output pixel and the
// picture's seed, so a held frame repeats exactly and a new picture
// boils.

float BegottenHash(float2 p, float seed)
{
    p += seed * float2(13.7, 7.3);
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Smooth value noise in [0, 1] over integer cells of p.
float BegottenValueNoise(float2 p, float seed)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = BegottenHash(cell, seed);
    float b = BegottenHash(cell + float2(1.0, 0.0), seed);
    float c = BegottenHash(cell + float2(0.0, 1.0), seed);
    float d = BegottenHash(cell + float2(1.0, 1.0), seed);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Three octaves of grain, centred on zero, at 'cell' output pixels per
// grain: a fine octave for the texture, a half-size octave for the
// bite, and a slow blob octave so whole patches of the boundary breathe.
float BegottenGrain(float2 outputPixel, float cell, float seed)
{
    float fine = BegottenValueNoise(outputPixel / cell, seed) - 0.5;
    float bite = BegottenValueNoise(outputPixel / (cell * 0.5), seed + 7.0) - 0.5;
    float blob = BegottenValueNoise(outputPixel / (cell * 2.3), seed * 0.37 + 3.0) - 0.5;
    return fine * 0.18 + bite * 0.08 + blob * 0.10;
}

// Distance from p to the segment a-b.
float BegottenSegmentDistance(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-4));
    return length(p - (a + ab * t));
}

// Dust on the stock: a white speck in the black, a dark speck in the
// white. Returns (white, dark) coverage in [0, 1].
float2 BegottenDust(float2 outputPixel, float seed)
{
    const float cellSize = 6.0;
    float2 cell = floor(outputPixel / cellSize);
    float roll = BegottenHash(cell, seed * 1.7 + 11.0);
    float2 speck =
        (cell + float2(
            BegottenHash(cell, seed + 3.0),
            BegottenHash(cell, seed + 5.0))) *
        cellSize;
    float coverage = smoothstep(1.7, 0.5, length(outputPixel - speck));
    float white = coverage * step(roll, 0.004);
    float dark = coverage * step(0.996, roll);
    return float2(white, dark);
}

// Hairs: short dark squiggles that lie on the gate for one picture.
// Evaluated over the 3x3 neighbourhood of 24-pixel cells so a hair is
// not cut at its cell's edge.
float BegottenHair(float2 outputPixel, float seed)
{
    const float cellSize = 24.0;
    float2 origin = floor(outputPixel / cellSize);
    float coverage = 0.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 cell = origin + float2(x, y);
            float roll = BegottenHash(cell, seed * 2.3 + 29.0);
            if (roll > 0.02)
            {
                continue;
            }

            float2 start =
                (cell + float2(
                    BegottenHash(cell, seed + 17.0),
                    BegottenHash(cell, seed + 19.0))) *
                cellSize;
            float angle = BegottenHash(cell, seed + 23.0) * 6.2831853;
            float bend = (BegottenHash(cell, seed + 31.0) - 0.5) * 1.6;
            float span = lerp(8.0, 16.0, BegottenHash(cell, seed + 37.0));
            float2 mid = start + float2(cos(angle), sin(angle)) * span * 0.5;
            float2 end = mid + float2(cos(angle + bend), sin(angle + bend)) * span * 0.5;
            float distance = min(
                BegottenSegmentDistance(outputPixel, start, mid),
                BegottenSegmentDistance(outputPixel, mid, end));
            coverage = max(coverage, smoothstep(1.3, 0.4, distance));
        }
    }

    return coverage;
}

// A scratch: a near-vertical line at scratch.x across the frame, with a
// wobble along its length and gaps, strongest in the middle of its
// life. scratch = (x, tone, life01, active). Returns coverage.
float BegottenScratch(
    float4 scratch,
    float2 windowUv,
    float texelWidth,
    float seed)
{
    if (scratch.w < 0.5)
    {
        return 0.0;
    }

    float wobble =
        (BegottenValueNoise(
            float2(windowUv.y * 40.0, scratch.x * 100.0),
            seed) -
         0.5) *
        texelWidth * 1.5;
    float distance = abs(windowUv.x - scratch.x - wobble);
    float width = texelWidth * 0.6;
    float strength = sin(scratch.z * 3.14159) * 0.9 + 0.1;
    float gaps = step(
        0.25,
        BegottenValueNoise(
            float2(windowUv.y * 25.0 + seed, scratch.x * 50.0),
            seed + 41.0));
    return smoothstep(width, width * 0.3, distance) * strength * gaps;
}

#endif
