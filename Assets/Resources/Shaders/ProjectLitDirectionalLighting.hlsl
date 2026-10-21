#ifndef BAR_PROMENADE_PROJECT_LIT_DIRECTIONAL_LIGHTING_INCLUDED
#define BAR_PROMENADE_PROJECT_LIT_DIRECTIONAL_LIGHTING_INCLUDED
#include "WarehouseCleanCommon.hlsl"
#include "DirectionalMetalLighting.hlsl"
#include "ProjectScreenSpaceReflection.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "WarehouseAmbientVisibility.hlsl"
#include "SceneSurfaceReflection.hlsl"

// Stock Lit reserves these CBUFFER slots for clear coat, which it never shades.
// Their explicit project meanings are opt-in anisotropy and its matte contact
// smoothness floor. The floor's sign opts selected surfaces into scene-local
// reflection, while its absolute value retains metal response unchanged.
// Reusing the layout keeps every URP pass SRP compatible.
static float2 projectLitSurfaceUv;

void ProjectLitSurfaceData(float2 uv, out SurfaceData surfaceData)
{
    InitializeStandardLitSurfaceData(uv, surfaceData);
#if defined(PROJECT_LIT_SURFACE_RESPONSE)
    PROJECT_LIT_SURFACE_RESPONSE(uv, surfaceData);
#endif
    projectLitSurfaceUv = uv;
    [branch] if (_ClearCoatMask > .001h && _WarehouseCleanEffects.y > 0)
        surfaceData.smoothness = lerp(surfaceData.smoothness,
            max(surfaceData.smoothness, min(abs(_ClearCoatSmoothness), .3h)),
            saturate(_WarehouseCleanEffects.y));
}

half4 ProjectLitFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
    directionalMetalAmount = min(_ClearCoatMask, .3h) * _WarehouseCleanEffects.y;
    [branch] if (directionalMetalAmount > 0)
    {
        float3 fallbackTangent = TransformObjectToWorldDir(float3(1, 0, 0));
#if defined(_NORMALMAP)
        fallbackTangent = inputData.tangentToWorld[0];
#endif
        DirectionalMetalSetFrame(inputData.positionWS, projectLitSurfaceUv,
            inputData.normalWS, fallbackTangent, directionalMetalAmount);
    }
    half4 color = WarehouseFragmentPBR(inputData, surfaceData);
    color.rgb += SceneSurfaceReflection(inputData, surfaceData);
    return color;
}
#endif
