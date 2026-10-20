#ifndef BAR_PROMENADE_WAREHOUSE_DIRECTIONAL_LIGHTING_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_DIRECTIONAL_LIGHTING_INCLUDED
#include "WarehouseCleanCommon.hlsl"
#include "DirectionalMetalLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

void WarehouseSetBrushFrame(float3 world, float2 uv, half3 normal)
{
    DirectionalMetalSetFrame(world, uv, normal, TransformObjectToWorldDir(float3(1, 0, 0)),
        _WarehouseAnisotropy * _WarehouseCleanEffects.y);
}
#endif
