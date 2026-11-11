#ifndef BAR_PROMENADE_CITY_WIND_SURFACE_INCLUDED
#define BARPROMENADE_CITY_WIND_SURFACE_INCLUDED

#include "CityWindField.hlsl"

float3 CityWindSurfaceOffset(float3 position, float3 root, float3 lateral, float2 cable,
    float4 wind, out float2 normalSlope)
{
    normalSlope = 0;
    if (_CityWindGeometry < .5 || wind.z <= 0)
        return 0;

    if (_CityWindGeometry > 1.5 && _CityWindGeometry < 2.5)
    {
        // The mesh carries the surveyed lateral vector with its pinned-end envelope.
        float2 axis = lateral.xz / max(length(lateral.xz), 1e-5);
        float force = dot(wind.xy, axis) * CityWindPulse(cable.y, wind.w);
        force += .12 * sin(wind.w * 3.8 + cable.y);
        return lateral * (CityWindStrength(wind.z, cable.x) * clamp(force, -1, 1));
    }

    float3 city = CityWindCoordinates(root);
    float phase = CityWindPhase(city.xz);
    bool shrub = _CityWindGeometry > 2.5;
    float reference = shrub ? .90 : 5.5;
    float height = max(0, position.y - root.y - (shrub ? .08 : 1.45));
    float lever = saturate(height / reference);
    float2 crosswind = float2(-wind.y, wind.x);
    float2 force = wind.xy * CityWindPulse(phase, wind.w) +
        crosswind * (.23 * sin(wind.w * 2.3 + phase * 1.3));
    force *= (shrub ? .18 : .72) * CityWindStrength(wind.z, city.y);
    normalSlope = force * (lever < 1 ? 2 * lever / reference : 0);
    return float3(force.x, 0, force.y) * lever * lever;
}

float3 CityWindSurfacePositionWS(float4 position, float4 motion, bool previous)
{
    float4x4 frame = previous ? UNITY_PREV_MATRIX_M : UNITY_MATRIX_M;
    float3 world = mul(frame, position).xyz;
    float3 root = mul(frame, float4(0, 0, 0, 1)).xyz;
    float3 lateral = mul((float3x3)frame, float3(motion.x, 0, motion.y));
    float2 normalSlope;
    return world + CityWindSurfaceOffset(world, root, lateral, motion.zw,
        previous ? _CityPreviousWind : _CityWind, normalSlope);
}

void CityWindSurfaceDisplace(inout float4 position, float4 motion)
{
    position.xyz = TransformWorldToObject(CityWindSurfacePositionWS(position, motion, false));
}

void CityWindSurfaceDisplaceNormal(inout float4 position, inout float3 normal, float4 motion)
{
    float3 world = TransformObjectToWorld(position.xyz);
    float3 root = TransformObjectToWorld(float3(0, 0, 0));
    float3 lateral = mul((float3x3)UNITY_MATRIX_M, float3(motion.x, 0, motion.y));
    float2 slope;
    world += CityWindSurfaceOffset(world, root, lateral, motion.zw, _CityWind, slope);
    float3 normalWS = TransformObjectToWorldNormal(normal);
    normalWS.y -= dot(slope, normalWS.xz);
    normal = TransformWorldToObjectNormal(normalize(normalWS));
    position.xyz = TransformWorldToObject(world);
}

#endif
