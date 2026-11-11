#ifndef BAR_PROMENADE_CITY_GRASS_RAIN_RESPONSE
#define BAR_PROMENADE_CITY_GRASS_RAIN_RESPONSE

int _CityRainGrassHitCount;
float4 _CityRainGrassHits[12];
float3 _CityRainView;

// A wet flash on the actual bent blade. Only the spots reached by the
// scene's first-hit rain are fed to the instanced grass material.
void ApplyCityGrassRain(float3 positionWS, inout SurfaceData surface)
{
    if (_CityRainGrassHitCount == 0 || distance(_CityRainView, _WorldSpaceCameraPos) > .1 ||
        distance(positionWS, _WorldSpaceCameraPos) > 11.0) return;
    half wet = 0.0h;
    [loop] for (int index = 0; index < _CityRainGrassHitCount; index++)
    {
        float4 hit = _CityRainGrassHits[index];
        float3 delta = positionWS - hit.xyz;
        if (delta.y < .04 || delta.y > 1.55 || dot(delta.xz, delta.xz) > .026) continue;
        float broken = frac(delta.y * 9.0 + hit.x * 17.31 + hit.z * 11.62);
        half bead = (half)((1.0 - smoothstep(.035, .16, length(delta.xz))) *
            smoothstep(.65, .89, broken) * (1.0 - hit.w));
        wet = max(wet, bead);
    }
    surface.albedo *= 1.0h - .13h * wet;
    surface.smoothness = lerp(surface.smoothness, .57h, wet);
}

#endif
