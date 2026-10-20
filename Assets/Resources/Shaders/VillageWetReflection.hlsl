#ifndef BAR_PROMENADE_VILLAGE_WET_REFLECTION_INCLUDED
#define BAR_PROMENADE_VILLAGE_WET_REFLECTION_INCLUDED
#include "WarehouseCleanCommon.hlsl"
TEXTURECUBE(_VillageReflectionCube); SAMPLER(sampler_VillageReflectionCube);
float4 _VillageReflectionDecode;
float4 _VillageReflectionRegion;
float4 _VillageReflectionMin;
float4 _VillageReflectionMax;

half3 VillageWetStoneReflection(InputData inputData, SurfaceData surfaceData, float family)
{
    float weight = _WarehouseCleanEffects.x * _VillageReflectionRegion.w;
    [branch] if (weight <= 0 || family < 5.5 || family > 6.5) return 0;
    float3 world = inputData.positionWS;
    float3 edge = min(world - _VillageReflectionMin.xyz, _VillageReflectionMax.xyz - world);
    [branch] if (any(edge < 0)) return 0;
    float fade = saturate(min(edge.x, edge.z) * 2.0) * saturate(edge.y * 5.0);
    float3 ray = reflect(-inputData.viewDirectionWS, inputData.normalWS);
    half4 sampleValue = SAMPLE_TEXTURECUBE_LOD(_VillageReflectionCube,
        sampler_VillageReflectionCube, ray, 3.0);
    half3 environment = DecodeHDREnvironment(sampleValue, _VillageReflectionDecode);
    float fresnel = .025 + .055 * Pow4(1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS)));
    return environment * (weight * fade * fresnel * surfaceData.occlusion);
}
#endif
