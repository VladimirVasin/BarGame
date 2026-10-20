#ifndef BAR_PROMENADE_ALPINE_LOCAL_LIGHTING_INCLUDED
#define BAR_PROMENADE_ALPINE_LOCAL_LIGHTING_INCLUDED

// Camera-scoped, bounded by the village provider; zero on every other camera.
int _AlpineLocalLightCount;
float4 _AlpineLocalLightPositions[4];
float4 _AlpineLocalLightDirections[4];
float4 _AlpineLocalLightColors[4];
float4 _AlpineLocalLightMinima[4];
float4 _AlpineLocalLightMaxima[4];
float4 _AlpineLocalLightPlanes[4];

float AlpineLocalLightSupport(int index, float3 world)
{
    float3 margins = min(world - _AlpineLocalLightMinima[index].xyz,
        _AlpineLocalLightMaxima[index].xyz - world);
    float edge = saturate(min(margins.x, min(margins.y, margins.z)) / .35);
    float4 plane = _AlpineLocalLightPlanes[index];
    float distanceToFacade = dot(plane.xyz, world) + plane.w;
    if (dot(plane.xyz, plane.xyz) > .5)
        edge *= saturate(distanceToFacade / .20);
    return edge;
}

float3 AlpineLocalLightRadiance(int index, float3 world)
{
    float support = AlpineLocalLightSupport(index, world);
    [branch] if (support <= 0) return 0;
    float3 delta = world - _AlpineLocalLightPositions[index].xyz;
    float squaredDistance = dot(delta, delta);
    float range = saturate(1 - squaredDistance * _AlpineLocalLightPositions[index].w);
    float attenuation = range * range / max(1.0, squaredDistance);
    float4 direction = _AlpineLocalLightDirections[index];
    if (direction.w > -.99)
    {
        float cone = saturate((dot(delta * rsqrt(max(squaredDistance, .0001)), direction.xyz) - direction.w) *
            _AlpineLocalLightColors[index].w);
        attenuation *= cone * cone;
    }
    return max(_AlpineLocalLightColors[index].rgb, 0) * (attenuation * support);
}

half3 AlpineLocalParticleLighting(float3 world)
{
    half3 result = 0;
    [loop] for (int index = 0; index < _AlpineLocalLightCount; index++)
    {
        float3 radiance = AlpineLocalLightRadiance(index, world);
        float peak = max(radiance.r, max(radiance.g, radiance.b));
        // Snow remains a discrete moving reflector, with bounded radiance at
        // the source. Its original alpha, shape, soft depth and fog survive.
        result += radiance * (.55 / (1 + peak * .35));
    }
    return result;
}
#endif
