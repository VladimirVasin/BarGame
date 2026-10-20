#ifndef BAR_PROMENADE_DIRECTIONAL_METAL_LIGHTING_INCLUDED
#define BAR_PROMENADE_DIRECTIONAL_METAL_LIGHTING_INCLUDED

// Share one direct-light BRDF between opt-in village metal and the warehouse.
// URP still owns light attenuation, shadows, visibility and Fresnel.
#define DirectBRDFSpecular DirectionalMetalOriginalBRDFSpecular
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#undef DirectBRDFSpecular

static float3 directionalMetalAxis = float3(1, 0, 0);
static half directionalMetalAmount = 0;

half DirectBRDFSpecular(BRDFData brdfData, half3 normalWS,
    half3 lightDirectionWS, half3 viewDirectionWS)
{
    half original = DirectionalMetalOriginalBRDFSpecular(brdfData, normalWS,
        lightDirectionWS, viewDirectionWS);
    [branch] if (directionalMetalAmount <= 0.0h) return original;
    float3 halfDirection = SafeNormalize(float3(lightDirectionWS) + float3(viewDirectionWS));
    float3 tangent = directionalMetalAxis - normalWS * dot(directionalMetalAxis, normalWS);
    tangent *= rsqrt(max(dot(tangent, tangent), 1e-6));
    float3 bitangent = cross(normalWS, tangent);
    // Replace the isotropic GGX distribution; never add a second highlight.
    float alpha = max(float(brdfData.roughness), .13);
    float aspect = sqrt(1.0 - .8 * min(float(directionalMetalAmount), .55));
    float ax = alpha / aspect, ay = alpha * aspect;
    float nh = saturate(dot(normalWS, halfDirection));
    float th = dot(tangent, halfDirection), bh = dot(bitangent, halfDirection);
    float ellipse = th * th / (ax * ax) + bh * bh / (ay * ay) + nh * nh;
    float anisoD = rcp(max(ax * ay * ellipse * ellipse, 1e-5));
    float denominator = nh * nh * (alpha * alpha - 1.0) + 1.0;
    float isotropicD = alpha * alpha / max(denominator * denominator, 1e-5);
    return original * clamp(anisoD / max(isotropicD, 1e-5), .15, 5.0);
}

void DirectionalMetalSetFrame(float3 world, float2 uv, half3 normal,
    float3 fallbackTangent, half amount)
{
    directionalMetalAmount = amount;
    [branch] if (amount <= 0.0h) return;
    float3 dx = ddx(world), dy = ddy(world);
    float2 ux = ddx(uv), uy = ddy(uv);
    float determinant = ux.x * uy.y - ux.y * uy.x;
    float3 tangent = fallbackTangent;
    if (abs(determinant) >= 1e-9)
        tangent = (dx * uy.y - dy * ux.y) / determinant;
    tangent -= normal * dot(tangent, normal);
    if (dot(tangent, tangent) < 1e-7)
        tangent = cross(normal, abs(normal.y) < .95 ? float3(0, 1, 0) : float3(0, 0, 1));
    directionalMetalAxis = tangent * rsqrt(max(dot(tangent, tangent), 1e-7));
}
#endif
