#ifndef BAR_PROMENADE_SCENE_WATER_REFLECTION_INCLUDED
#define BAR_PROMENADE_SCENE_WATER_REFLECTION_INCLUDED

// Reuse the scene's two finite static environments. Water already owning
// its fountain/spring cubemap keeps that separate authored reflection.
TEXTURECUBE(_SceneSurfaceReflectionCube0);
TEXTURECUBE(_SceneSurfaceReflectionCube1);
int _SceneSurfaceReflectionCount;
float4 _SceneSurfaceReflectionOrigins[2];
float4 _SceneSurfaceReflectionMinima[2];
float4 _SceneSurfaceReflectionMaxima[2];
float4 _SceneSurfaceReflectionDecode[2];

half3 SceneWaterReflection(float3 world, float3 normal, float3 viewDirection)
{
    [branch] if (_WarehouseCleanEffects.x <= 0 || _SceneSurfaceReflectionCount <= 0) return 0;
    [unroll] for (int zone = 0; zone < 2; zone++)
    {
        [branch] if (zone >= _SceneSurfaceReflectionCount) break;
        float4 origin = _SceneSurfaceReflectionOrigins[zone];
        [branch] if (origin.w <= 0) continue;
        float3 minimum = _SceneSurfaceReflectionMinima[zone].xyz;
        float3 maximum = _SceneSurfaceReflectionMaxima[zone].xyz;
        float3 edge = min(world - minimum, maximum - world);
        [branch] if (any(edge < 0)) continue;
        // The scene outside the practical's region stays dark. A short
        // edge fade also avoids a rectangular reflection on a continuous river.
        float fade = saturate(min(edge.x, edge.z) * .75);
        float3 reflected = reflect(-viewDirection, normal);
        float3 safeRay = (step(0.0, reflected) * 2.0 - 1.0) * max(abs(reflected), 1e-5);
        float3 toBox = max((maximum - world) / safeRay, (minimum - world) / safeRay);
        float distanceToWall = min(toBox.x, min(toBox.y, toBox.z));
        float3 sampleRay = world + reflected * max(0.0, distanceToWall) - origin.xyz;
        half4 encoded;
        if (zone == 0)
            encoded = SAMPLE_TEXTURECUBE_LOD(_SceneSurfaceReflectionCube0, sampler_LinearClamp, sampleRay, 2.5);
        else
            encoded = SAMPLE_TEXTURECUBE_LOD(_SceneSurfaceReflectionCube1, sampler_LinearClamp, sampleRay, 2.5);
        half3 environment = DecodeHDREnvironment(encoded, _SceneSurfaceReflectionDecode[zone]);
        float fresnel = .035 + .18 * Pow4(1.0 - saturate(dot(normal, viewDirection)));
        return environment * (origin.w * fade * fresnel);
    }
    return 0;
}
#endif
