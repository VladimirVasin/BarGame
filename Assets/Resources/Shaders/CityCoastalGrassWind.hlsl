#ifndef BAR_PROMENADE_COASTAL_GRASS_WIND_INCLUDED
#define BAR_PROMENADE_COASTAL_GRASS_WIND_INCLUDED

// Per-chunk metadata on the existing flat-colour stems: UV.x selects the
// authored root; UV.y is exactly zero on every rooted base-plane vertex.
// Kept outside UnityPerMaterial to retain the pinned Lit layout in every pass.
float4 _CoastalGrassRoots[42];
float4 _CoastalGrassWind;        // direction XZ, strength, scaled phase
float4 _CoastalGrassInteraction; // hero world XYZ, one bounded radius
float4 _CoastalGrassMotion;      // enabled, wind cap, contact cap, vertical reach

void CoastalGrassDisplace(inout float4 positionOS, float2 metadata)
{
    float weight = saturate(metadata.y);
    if (_CoastalGrassMotion.x <= 0 || weight <= 0) return;
    float4 stem = _CoastalGrassRoots[(int)metadata.x];
    float3 root = TransformObjectToWorld(stem.xyz);
    float phase = dot(root.xz, float2(.71, .53));
    float wave = .65 + .25 * sin(_CoastalGrassWind.w * 1.7 + phase) +
        .10 * sin(_CoastalGrassWind.w * 3.8 + phase * 1.41);
    float2 windDirection = _CoastalGrassWind.xy;
    float2 travel = windDirection * (_CoastalGrassMotion.y * _CoastalGrassWind.z * wave);
    float2 away = root.xz - _CoastalGrassInteraction.xz;
    float distance = length(away);
    if (_CoastalGrassInteraction.w > 0 && abs(root.y - _CoastalGrassInteraction.y) < _CoastalGrassMotion.w)
    {
        float influence = saturate(1 - distance / _CoastalGrassInteraction.w);
        influence = influence * influence * (3 - 2 * influence);
        // A soft centre keeps the radial response continuous as a foot crosses
        // a root; normalising an almost-zero vector would flip a stem instantly.
        float2 direction = away / max(distance, .12);
        travel += direction * (_CoastalGrassMotion.z * influence);
    }
    float height = max(stem.w, .1);
    float sink = min(height * .18, dot(travel, travel) / (2 * height));
    float3 offset = float3(travel.x, -sink, travel.y) * (weight * weight);
    positionOS.xyz = TransformWorldToObject(TransformObjectToWorld(positionOS.xyz) + offset);
}
#endif
