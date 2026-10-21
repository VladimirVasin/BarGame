#ifndef BAR_PROMENADE_SCENE_SURFACE_REFLECTION_INCLUDED
#define BAR_PROMENADE_SCENE_SURFACE_REFLECTION_INCLUDED

TEXTURECUBE(_SceneSurfaceReflectionCube0);
TEXTURECUBE(_SceneSurfaceReflectionCube1);
int _SceneSurfaceReflectionCount;
float4 _SceneSurfaceReflectionOrigins[2];
float4 _SceneSurfaceReflectionMinima[2];
float4 _SceneSurfaceReflectionMaxima[2];
float4 _SceneSurfaceReflectionDecode[2];

half3 SceneSurfaceReflection(InputData input, SurfaceData surface)
{
    // A negative reserved contact-floor slot explicitly allows reflection;
    // its absolute value remains the unchanged directional-contact floor.
    // Camera flags are zero for probes, previews and other scene cameras.
    [branch] if (_ClearCoatSmoothness >= 0 || _WarehouseCleanEffects.x <= 0 ||
        _SceneSurfaceReflectionCount <= 0) return 0;
#if defined(_ENVIRONMENTREFLECTIONS_OFF)
    return 0;
#endif
    [unroll] for (int zone = 0; zone < 2; zone++)
    {
        [branch] if (zone >= _SceneSurfaceReflectionCount) break;
        float4 origin = _SceneSurfaceReflectionOrigins[zone];
        [branch] if (origin.w <= 0) continue;
        float3 minimum = _SceneSurfaceReflectionMinima[zone].xyz;
        float3 maximum = _SceneSurfaceReflectionMaxima[zone].xyz;
        float3 world = input.positionWS;
        [branch] if (any(world < minimum) || any(world > maximum)) continue;
        float3 reflected = reflect(-input.viewDirectionWS, input.normalWS);
        float3 safeRay = (step(0.0, reflected) * 2.0 - 1.0) * max(abs(reflected), 1e-5);
        float3 distanceToBox = max((maximum - world) / safeRay, (minimum - world) / safeRay);
        float wallDistance = min(distanceToBox.x, min(distanceToBox.y, distanceToBox.z));
        float3 sampleRay = world + reflected * max(0.0, wallDistance) - origin.xyz;
        // Use the actual authored roughness with URP's filtered glossy levels.
        float roughness = max(1.0 - surface.smoothness, .12);
        float mip = roughness * (1.7 - .7 * roughness) * 6.0;
        half4 encoded;
        if (zone == 0)
            encoded = SAMPLE_TEXTURECUBE_LOD(_SceneSurfaceReflectionCube0,
                sampler_LinearClamp, sampleRay, mip);
        else
            encoded = SAMPLE_TEXTURECUBE_LOD(_SceneSurfaceReflectionCube1,
                sampler_LinearClamp, sampleRay, mip);
        half3 environment = DecodeHDREnvironment(encoded, _SceneSurfaceReflectionDecode[zone]);
#if defined(_SPECULAR_SETUP)
        half3 f0 = surface.specular;
#else
        half3 f0 = lerp(half3(.04h, .04h, .04h), surface.albedo, surface.metallic);
#endif
        half3 fresnel = f0 + (1.0h - f0) * Pow4(1.0h - saturate(dot(input.normalWS, input.viewDirectionWS)));
        half fallback = 1.0h - ProjectScreenSpaceConfidence(ProjectScreenSpaceUv(world),
            world, surface.smoothness);
        return environment * fresnel * (origin.w * surface.occlusion * fallback *
            projectReflectionFallbackWeight / (1.0 + roughness * roughness));
    }
    return 0;
}
#endif
