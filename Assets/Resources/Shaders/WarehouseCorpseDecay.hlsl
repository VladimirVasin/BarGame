#ifndef BAR_PROMENADE_WAREHOUSE_CORPSE_DECAY_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_CORPSE_DECAY_INCLUDED

// Callers declare _CorpseDecay / _CorpseSeed in their material buffer. The same
// UV0 pattern clips body surfaces and procedural wound overlays together.
// No screen coordinates or shader clock: pause, shadows and reflections agree.
float WarehouseCorpseHash(float2 cell)
{
    float3 value = frac(float3(cell.xyx) * 0.1031 + _CorpseSeed * 0.017);
    value += dot(value, value.yzx + 33.33);
    return frac((value.x + value.y) * value.z);
}

float WarehouseCorpseNoise(float2 uv)
{
    float2 cell = floor(uv);
    float2 blend = frac(uv);
    blend = blend * blend * (3.0 - 2.0 * blend);
    return lerp(lerp(WarehouseCorpseHash(cell), WarehouseCorpseHash(cell + float2(1, 0)), blend.x),
        lerp(WarehouseCorpseHash(cell + float2(0, 1)), WarehouseCorpseHash(cell + 1), blend.x), blend.y);
}

half WarehouseCorpseBreakup(float2 uv)
{
    [branch] if (_CorpseDecay <= 0.0h) return 1.0h;
    float coarse = WarehouseCorpseNoise(uv * 22.0);
    float fine = WarehouseCorpseNoise(uv * 79.0 + 7.13);
    float material = 0.04 + 0.92 * lerp(coarse, fine, 0.24);
    float edge = material - saturate(_CorpseDecay) * 1.02;
    clip(edge);
    // The broken edge is dark matter under the existing light, never emission.
    return lerp(0.58h, 1.0h, smoothstep(0.0, 0.12, edge));
}
#endif
