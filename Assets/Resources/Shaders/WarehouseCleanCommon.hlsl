#ifndef BAR_PROMENADE_WAREHOUSE_CLEAN_COMMON_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_CLEAN_COMMON_INCLUDED

// Camera-scoped by the renderer, outside UnityPerMaterial and zero in captures.
float4 _WarehouseCleanEffects;
int _WarehouseBounceCount;
float4 _WarehouseBounceCenters[4];
float4 _WarehouseBounceColors[4];
float4 _WarehouseBounceMin[4];
float4 _WarehouseBounceMax[4];

half3 WarehouseLocalBounce(float3 world, half3 normal)
{
    [branch] if (_WarehouseCleanEffects.z <= 0.0) return 0;
    half3 irradiance = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        [branch] if (i >= _WarehouseBounceCount) break;
        float3 lo = world - _WarehouseBounceMin[i].xyz;
        float3 hi = _WarehouseBounceMax[i].xyz - world;
        [branch] if (any(lo < 0) || any(hi < 0)) continue;
        float3 toPatch = _WarehouseBounceCenters[i].xyz - world;
        float squared = dot(toPatch, toPatch);
        float3 incoming = toPatch * rsqrt(max(squared, .04));
        float reach = saturate(1.0 - squared /
            (_WarehouseBounceCenters[i].w * _WarehouseBounceCenters[i].w));
        float emitter = saturate(-incoming.y);
        float receiver = saturate(dot(normal, incoming));
        // The AABB stops transport behind the first occluder. Do not fade at
        // that clipped face: it is the actual receiver, not a free-space edge.
        irradiance += _WarehouseBounceColors[i].rgb * receiver * emitter * reach * reach;
    }
    return irradiance * _WarehouseCleanEffects.z;
}
#endif
