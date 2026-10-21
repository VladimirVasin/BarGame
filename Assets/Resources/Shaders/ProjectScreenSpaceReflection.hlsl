#ifndef BAR_PROMENADE_SCREEN_SPACE_REFLECTION_INCLUDED
#define BAR_PROMENADE_SCREEN_SPACE_REFLECTION_INCLUDED

// Reuse the native sample/BRDF once, but admit only the selected wet receiver
// in each fragment. Other project Lit materials keep their existing probes.
#define GetScreenSpaceReflection ProjectNativeGetScreenSpaceReflection
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SampleScreenSpaceReflection.hlsl"
#undef GetScreenSpaceReflection

float _SceneScreenSpaceReflectionReady;
static half projectScreenSpaceReceiver = 0;
static half projectReflectionFallbackWeight = 1;

float2 ProjectScreenSpaceUv(float3 world)
{
    // The native SSR tracer uses this same divide-by-W and platform Y convention.
    // GetNormalizedScreenSpaceUV instead expects SV_POSITION in raster pixels.
    return ComputeNormalizedDeviceCoordinates(world, UNITY_MATRIX_VP);
}

half4 GetScreenSpaceReflection(float2 uv, float3 world, float roughness)
{
#if !defined(_SCREENSPACEREFLECTIONS_OFF)
    half weight = saturate(projectScreenSpaceReceiver * _SceneScreenSpaceReflectionReady);
    [branch] if (weight > .0001h)
    {
        half4 sample = ProjectNativeGetScreenSpaceReflection(uv, world, roughness);
        // RGB is straight native radiance. Only confidence blends the BRDF's
        // environment; multiplying RGB here would darken it a second time.
        sample.a *= weight;
        return sample;
    }
#endif
    return 0;
}

half ProjectScreenSpaceConfidence(float2 uv, float3 world, half smoothness)
{
    return saturate(GetScreenSpaceReflection(uv, world, 1.0h - smoothness).a);
}
#endif
