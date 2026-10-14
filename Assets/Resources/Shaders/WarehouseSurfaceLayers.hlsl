#ifndef BAR_PROMENADE_WAREHOUSE_SURFACE_LAYERS_INCLUDED
#define BAR_PROMENADE_WAREHOUSE_SURFACE_LAYERS_INCLUDED

TEXTURE2D(_WarehouseMacroMask); SAMPLER(sampler_WarehouseMacroMask);
TEXTURE2D(_WarehouseTriplanarMap); SAMPLER(sampler_WarehouseTriplanarMap);

// One extra mask sample on structural surfaces. No procedural/time noise:
// stains stay fixed to their physical place through camera motion and reset.
void ApplyWarehouseSurface(float3 world, half3 normal, inout SurfaceData surface)
{
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
#endif
