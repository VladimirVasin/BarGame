#ifndef BAR_PROMENADE_WAREHOUSE_SURFACE_LAYERS_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_SURFACE_LAYERS_INCLUDED

TEXTURE2D(_WarehouseMacroMask); SAMPLER(sampler_WarehouseMacroMask);
TEXTURE2D(_WarehouseTriplanarMap); SAMPLER(sampler_WarehouseTriplanarMap);
TEXTURE2D(_WarehouseDetailMap); SAMPLER(sampler_WarehouseDetailMap);
TEXTURE2D(_WarehouseMaterialResponse); SAMPLER(sampler_WarehouseMaterialResponse);

// Bounded material sampling: response + unique-prop detail OR structure macro.
// No procedural/time noise; authored wear and grain follow the physical part.
void ApplyWarehouseSurface(float3 world, half3 normal, float2 metres,
    inout SurfaceData surface, out half2 heightSlope)
{
    heightSlope = half2(0, 0);
    float2 detailUv = metres * _WarehouseDetailScale;
    [branch] if (_WarehouseDetailStrength > 0.0h)
    {
        // The unique UV0 owns placement and wear; UV1 restores material grain
        // without requiring another atlas, changing its tint or UV0 tangents.
        half3 detail = SAMPLE_TEXTURE2D(_WarehouseDetailMap, sampler_WarehouseDetailMap, detailUv).rgb;
        half3 variation = clamp(detail / max(_WarehouseDetailMean.rgb, 0.005h), 0.58h, 1.60h);
        surface.albedo *= lerp(half3(1, 1, 1), variation, _WarehouseDetailStrength);
    }
    [branch] if (_WarehouseResponseStrength > 0.0h)
    {
        // Gradients precede frac so cell boundaries neither select a wrong mip
        // nor sample a neighbouring material. Every cell has wrapped padding.
        float2 responseUv = frac(detailUv) * _WarehouseResponseRect.xy + _WarehouseResponseRect.zw;
        half4 response = SAMPLE_TEXTURE2D_GRAD(_WarehouseMaterialResponse, sampler_WarehouseMaterialResponse,
            responseUv, ddx(detailUv) * _WarehouseResponseRect.xy, ddy(detailUv) * _WarehouseResponseRect.xy);
        half nearFade = 1.0h - smoothstep(3.0, 9.0, distance(world, GetCameraPositionWS()));
        heightSlope = (response.rg * 2.0h - 1.0h) * _WarehouseNormalStrength * nearFade;
        surface.smoothness = lerp(surface.smoothness, 1.0h - response.b, _WarehouseResponseStrength * 0.65h);
        surface.occlusion *= lerp(1.0h, response.a, _WarehouseResponseStrength * 0.65h);
    }
    float3 coordinates = world;
    half3 projectionNormal = normalize(normal);
    [branch] if (_WarehouseTriplanarStrength > 0.0h)
    {
        // The authoring FBX root carries its centimetre-to-metre factor.
        // Object projection follows a moving/deformed part, never the camera.
        coordinates = TransformWorldToObject(world) * _WarehouseObjectMetres;
        projectionNormal = normalize(TransformWorldToObjectDir(normal));
        half3 weights = pow(abs(projectionNormal), 4.0h);
        weights /= max(dot(weights, half3(1, 1, 1)), 0.001h);
        half3 tri =
            SAMPLE_TEXTURE2D(_WarehouseTriplanarMap, sampler_WarehouseTriplanarMap, coordinates.zy).rgb * weights.x +
            SAMPLE_TEXTURE2D(_WarehouseTriplanarMap, sampler_WarehouseTriplanarMap, coordinates.xz).rgb * weights.y +
            SAMPLE_TEXTURE2D(_WarehouseTriplanarMap, sampler_WarehouseTriplanarMap, coordinates.xy).rgb * weights.z;
        surface.albedo = lerp(surface.albedo, tri * _BaseColor.rgb, _WarehouseTriplanarStrength);
    }
    [branch] if (_WarehouseMacroStrength > 0.0h || _WarehouseRustStrength > 0.0h)
    {
        float2 plane = abs(projectionNormal.y) > 0.5h ? coordinates.xz :
            (abs(projectionNormal.x) > abs(projectionNormal.z) ? coordinates.zy : coordinates.xy);
        half3 mask = SAMPLE_TEXTURE2D(_WarehouseMacroMask, sampler_WarehouseMacroMask,
            plane * _WarehouseMacroScale + float2(0.137, 0.293)).rgb;
        half damp = smoothstep(0.28h, 0.79h, mask.r) * _WarehouseMacroStrength;
        half rust = smoothstep(0.34h, 0.86h, mask.g) * _WarehouseRustStrength;
        half scuff = smoothstep(0.35h, 0.82h, mask.b) * _WarehouseMacroStrength;
        // Damp preserves the material's identity; rust is old ochre, never flesh.
        surface.albedo *= lerp(half3(0.96h, 0.98h, 0.94h), half3(0.58h, 0.67h, 0.61h), damp);
        surface.albedo = lerp(surface.albedo, surface.albedo * half3(1.13h, 0.63h, 0.36h), rust);
        surface.albedo *= 1.0h + scuff * 0.065h;
        surface.smoothness = lerp(surface.smoothness, min(0.24h, surface.smoothness + 0.10h), damp);
        surface.smoothness *= 1.0h - rust * 0.65h;
        surface.metallic *= 1.0h - rust * 0.70h;
    }
}

void ApplyWarehouseNormal(float3 world, float2 metres, half2 slope, inout half3 normal)
{
    [branch] if (_WarehouseNormalStrength <= 0.0h) return;
    // A derivative frame built from METRE UV1, independent of rotated or
    // mirrored atlas UV0 and absent imported tangents. World derivatives carry
    // the current rigid part pose, so relief follows the fan and drum.
    float3 dx = ddx(world), dy = ddy(world);
    float2 ux = ddx(metres), uy = ddy(metres);
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
