#ifndef BAR_PROMENADE_WAREHOUSE_DIRECTIONAL_LIGHTING_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_DIRECTIONAL_LIGHTING_INCLUDED
#include "WarehouseCleanCommon.hlsl"

// Compile URP's original specular function once under a private name. Its
// lighting loops below retain all real light/shadow/cookie attenuation.
#define DirectBRDFSpecular WarehouseOriginalBRDFSpecular
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#undef DirectBRDFSpecular

static float3 warehouseBrushAxis = float3(1, 0, 0);
static half warehouseBrushAmount = 0;

half DirectBRDFSpecular(BRDFData brdfData, half3 normalWS,
    half3 lightDirectionWS, half3 viewDirectionWS)
{
    half original = WarehouseOriginalBRDFSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS);
    [branch] if (warehouseBrushAmount <= 0.0h) return original;
    float3 halfDirection = SafeNormalize(float3(lightDirectionWS) + float3(viewDirectionWS));
    float3 tangent = warehouseBrushAxis - normalWS * dot(warehouseBrushAxis, normalWS);
    tangent *= rsqrt(max(dot(tangent, tangent), 1e-6));
    float3 bitangent = cross(normalWS, tangent);
    // An elliptic GGX NDF replaces the isotropic NDF. The existing visibility
    // and Fresnel approximation are retained; there is no second specular lobe.
    float alpha = max(float(brdfData.roughness), .13);
    float aspect = sqrt(1.0 - .8 * min(float(warehouseBrushAmount), .55));
    float ax = alpha / aspect, ay = alpha * aspect;
    float nh = saturate(dot(normalWS, halfDirection));
    float th = dot(tangent, halfDirection), bh = dot(bitangent, halfDirection);
    float ellipse = th * th / (ax * ax) + bh * bh / (ay * ay) + nh * nh;
    float anisoD = rcp(max(ax * ay * ellipse * ellipse, 1e-5));
    float denominator = nh * nh * (alpha * alpha - 1.0) + 1.0;
    float isotropicD = alpha * alpha / max(denominator * denominator, 1e-5);
    return original * clamp(anisoD / max(isotropicD, 1e-5), .15, 5.0);
}
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

void WarehouseSetBrushFrame(float3 world, float2 uv, half3 normal)
{
    warehouseBrushAmount = _WarehouseAnisotropy * _WarehouseCleanEffects.y;
    [branch] if (warehouseBrushAmount <= 0.0h) return;
    float3 dx = ddx(world), dy = ddy(world);
    float2 ux = ddx(uv), uy = ddy(uv);
    float determinant = ux.x * uy.y - ux.y * uy.x;
    if (abs(determinant) < 1e-9) { warehouseBrushAmount = 0; return; }
    float3 tangent = (dx * uy.y - dy * ux.y) / determinant;
    tangent -= normal * dot(tangent, normal);
    warehouseBrushAxis = tangent * rsqrt(max(dot(tangent, tangent), 1e-7));
}
#endif
