#ifndef BAR_PROMENADE_SURFACE_MATERIAL_RESPONSE_INCLUDED
#define BAR_PROMENADE_SURFACE_MATERIAL_RESPONSE_INCLUDED

TEXTURE2D(_SceneMaterialResponse); SAMPLER(sampler_SceneMaterialResponse);
static half2 sceneMaterialHeightSlope = half2(0, 0);

// One aligned response sample. RG stores signed d(height metres)/d(surface
// metres) along authored UV axes, B roughness, A cavity. No albedo inference.
void ApplySceneMaterialResponse(float2 uv, inout SurfaceData surface)
{
    sceneMaterialHeightSlope = half2(0, 0);
    [branch] if (_SceneResponseStrength <= 0.0h && _SceneNormalStrength <= 0.0h) return;
    half4 response = SAMPLE_TEXTURE2D(_SceneMaterialResponse, sampler_SceneMaterialResponse, uv);
    sceneMaterialHeightSlope = response.rg * 2.0h - 1.0h;
    half strength = saturate(_SceneResponseStrength);
    surface.smoothness = lerp(surface.smoothness, 1.0h - response.b, strength);
    surface.occlusion *= lerp(1.0h, response.a, strength);
}

void ApplySceneMaterialNormal(float3 world, float2 uv, half2 slope, inout half3 normal)
{
    [branch] if (_SceneNormalStrength <= 0.0h) return;
    half nearFade = 1.0h - smoothstep(3.0, 9.0, distance(world, GetCameraPositionWS()));
    slope *= _SceneNormalStrength * nearFade;
    // Derivatives retain each part's live orientation, including the rocking
    // chair. Unique charts use equal metre scale along U/V, not repeating detail.
    float3 dx = ddx(world), dy = ddy(world);
    float2 ux = ddx(uv), uy = ddy(uv);
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
