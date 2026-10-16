#ifndef BAR_PROMENADE_WAREHOUSE_SURFACE_LAYERS_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_SURFACE_LAYERS_INCLUDED

TEXTURE2D(_WarehouseMaterialResponse); SAMPLER(sampler_WarehouseMaterialResponse);

float2 WarehouseSurfaceUv(float2 atlasUv, float2 metres)
{
    // Auxiliary moving props keep a bounded patch of the same sheet. The
    // warehouse itself owns fully unique authored UV0, including every copy.
    [branch] if (_WarehouseAuxiliary > 0.0h)
        return saturate(metres * 0.12 + 0.5) * _WarehouseAuxRect.xy + _WarehouseAuxRect.zw;
    return atlasUv;
}

// One colour sample in LitInput plus one aligned response sample. Both describe
// the same authored wear; there are no repeating colour/detail/macro layers.
void ApplyWarehouseSurface(float3 world, float2 atlasUv,
    inout SurfaceData surface, out half2 heightSlope)
{
    heightSlope = half2(0, 0);
    [branch] if (_WarehouseResponseStrength > 0.0h)
    {
        half4 response = SAMPLE_TEXTURE2D(_WarehouseMaterialResponse, sampler_WarehouseMaterialResponse, atlasUv);
        half nearFade = 1.0h - smoothstep(3.0, 9.0, distance(world, GetCameraPositionWS()));
        heightSlope = (response.rg * 2.0h - 1.0h) * _WarehouseNormalStrength * nearFade;
        surface.smoothness = lerp(surface.smoothness, 1.0h - response.b, _WarehouseResponseStrength);
        surface.occlusion *= lerp(1.0h, response.a, _WarehouseResponseStrength);
    }
}

void ApplyWarehouseNormal(float3 world, float2 atlasUv, half2 slope, inout half3 normal)
{
    [branch] if (_WarehouseNormalStrength <= 0.0h) return;
    // Slopes were authored in atlas orientation, so the derivative frame uses
    // that same UV0. World derivatives retain each moving part's current pose.
    float3 dx = ddx(world), dy = ddy(world);
    float2 ux = ddx(atlasUv), uy = ddy(atlasUv);
    float determinant = ux.x * uy.y - ux.y * uy.x;
    float safeDeterminant = (determinant < 0.0 ? -1.0 : 1.0) * max(abs(determinant), 1e-10);
    float3 tangent = (dx * uy.y - dy * ux.y) / safeDeterminant;
    float3 bitangent = (dy * ux.x - dx * uy.x) / safeDeterminant;
    tangent -= normal * dot(tangent, normal);
    bitangent -= normal * dot(bitangent, normal);
    tangent *= rsqrt(max(dot(tangent, tangent), 1e-8));
    bitangent *= rsqrt(max(dot(bitangent, bitangent), 1e-8));
    half valid = abs(determinant) > 1e-10 ? 1.0h : 0.0h;
    normal = normalize(normal - (tangent * slope.x + bitangent * slope.y) * valid);
}
#endif
