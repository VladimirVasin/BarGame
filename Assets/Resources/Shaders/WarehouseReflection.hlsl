#ifndef BAR_PROMENADE_WAREHOUSE_REFLECTION_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_REFLECTION_INCLUDED
#include "WarehouseCleanCommon.hlsl"
TEXTURECUBE(_WarehouseReflectionCube); SAMPLER(sampler_WarehouseReflectionCube);
float4 _WarehouseReflectionDecode;
float4 _WarehouseReflectionProbe;
float4 _WarehouseReflectionBoxMin;
float4 _WarehouseReflectionBoxMax;

half WarehouseWetPatchMask(float3 world, half3 normal)
{
    if (_WarehouseWetReflection <= 0 || normal.y < .72h || abs(world.y) > .12) return 0;
    float3 minimum = _WarehouseReflectionBoxMin.xyz, maximum = _WarehouseReflectionBoxMax.xyz;
    if (any(world < minimum) || any(world > maximum)) return 0;
    float patch = sin(world.x * .81 + world.z * .37) * sin(world.z * .53 - world.x * .21);
    half wet = smoothstep(.12, .46, patch);
    half edge = saturate(min(min(world.x - minimum.x, maximum.x - world.x),
        min(world.z - minimum.z, maximum.z - world.z)) * 1.2);
    return saturate(wet * edge);
}

half WarehouseScreenSpaceReceiver(float3 world, half3 normal)
{
    return saturate(_WarehouseCleanEffects.x * _WarehouseReflectionProbe.w * _WarehouseWetReflection *
        WarehouseWetPatchMask(world, normal));
}

void ApplyWarehouseWetSmoothness(float3 world, half3 normal, inout SurfaceData surface)
{
    // The existing floor wetness already reflects an environment. Feed the same
    // film into both physical shading and SSR's depth-normal smoothness channel.
    half wet = WarehouseWetPatchMask(world, normal);
    surface.smoothness = lerp(surface.smoothness, max(surface.smoothness, .55h), wet);
}

half3 WarehouseWetReflection(float3 world, half3 normal, half3 view, SurfaceData surface)
{
    float weight = _WarehouseCleanEffects.x * _WarehouseReflectionProbe.w * _WarehouseWetReflection;
    [branch] if (weight <= 0.0 || normal.y < .72 || abs(world.y) > .12) return 0;
    float3 minimum = _WarehouseReflectionBoxMin.xyz;
    float3 maximum = _WarehouseReflectionBoxMax.xyz;
    [branch] if (any(world < minimum) || any(world > maximum)) return 0;
    // Sparse broad wet patches, stable in metres and shared by every camera.
    float patch = sin(world.x * .81 + world.z * .37) *
        sin(world.z * .53 - world.x * .21);
    float wet = smoothstep(.12, .46, patch);
    float edge = saturate(min(min(world.x - minimum.x, maximum.x - world.x),
        min(world.z - minimum.z, maximum.z - world.z)) * 1.2);
    float3 reflected = reflect(-view, normal);
    float3 safeRay = (step(0.0, reflected) * 2.0 - 1.0) * max(abs(reflected), 1e-5);
    float3 distanceToBox = max((maximum - world) / safeRay, (minimum - world) / safeRay);
    float distanceToWall = min(distanceToBox.x, min(distanceToBox.y, distanceToBox.z));
    float3 sampleRay = world + reflected * max(0.0, distanceToWall) - _WarehouseReflectionProbe.xyz;
    float roughness = max(1.0 - surface.smoothness, .24);
    // URP's filtered realtime probe stores successive glossy convolution levels.
    float mip = roughness * (1.7 - .7 * roughness) * 6.0;
    half4 encoded = SAMPLE_TEXTURECUBE_LOD(_WarehouseReflectionCube,
        sampler_WarehouseReflectionCube, sampleRay, mip);
    half3 reflectedColor = DecodeHDREnvironment(encoded, _WarehouseReflectionDecode);
    half fresnel = .04h + .22h * Pow4(1.0h - saturate(dot(normal, view)));
    half fallback = 1.0h - ProjectScreenSpaceConfidence(ProjectScreenSpaceUv(world), world, surface.smoothness);
    return reflectedColor * (fresnel * wet * edge * weight * surface.occlusion * fallback);
}
#endif
