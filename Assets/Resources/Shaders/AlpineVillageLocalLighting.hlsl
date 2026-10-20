#ifndef BAR_PROMENADE_ALPINE_LOCAL_LIGHTING_INCLUDED
#define BAR_PROMENADE_ALPINE_LOCAL_LIGHTING_INCLUDED

// Camera-scoped finite sources; legacy village descriptors retain their path.
int _AlpineLocalLightCount;
float4 _AlpineLocalLightPositions[4];
float4 _AlpineLocalLightDirections[4];
float4 _AlpineLocalLightColors[4];
float4 _AlpineLocalLightMinima[4];
float4 _AlpineLocalLightMaxima[4];
float4 _AlpineLocalLightPlanes[4];
// gain, sample existing shadows, additional clip-plane count, blocker count.
float4 _AlpineLocalLightOptions[4];
float4 _AlpineLocalLightClipPlanes[24];
float4 _AlpineLocalLightBlockerMinima[32];
float4 _AlpineLocalLightBlockerMaxima[32];
float4 _AlpineLocalLightShadowIndices;
int _AlpineLocalLightingPerPixel;

bool AlpineLocalBoxInterval(float3 minimum, float3 maximum, float3 origin, float3 direction,
    out float enter, out float leave)
{
    float3 inverse = (step(0.0, direction) * 2.0 - 1.0) / max(abs(direction), .00001);
    float3 a = (minimum - origin) * inverse, b = (maximum - origin) * inverse;
    float3 nearPlane = min(a, b), farPlane = max(a, b);
    enter = max(0.0, max(nearPlane.x, max(nearPlane.y, nearPlane.z)));
    leave = min(farPlane.x, min(farPlane.y, farPlane.z));
    return leave > enter;
}

float AlpineLocalStructuralVisibility(int index, float3 world)
{
    int count = (int)_AlpineLocalLightOptions[index].w;
    if (count == 0) return 1;
    float3 origin = _AlpineLocalLightPositions[index].xyz;
    float3 delta = world - origin;
    float distanceToSample = length(delta);
    if (distanceToSample < .05) return 1;
    float3 direction = delta / distanceToSample;
    float3 segmentMin = min(origin, world), segmentMax = max(origin, world);
    [loop] for (int blocker = 0; blocker < count; blocker++)
    {
        int entry = index * 8 + blocker;
        float3 minimum = _AlpineLocalLightBlockerMinima[entry].xyz;
        float3 maximum = _AlpineLocalLightBlockerMaxima[entry].xyz;
        if (any(maximum <= minimum) || any(segmentMax < minimum) || any(segmentMin > maximum)) continue;
        float enter, leave;
        if (AlpineLocalBoxInterval(minimum, maximum, origin, direction, enter, leave) &&
            leave > .025 && enter < distanceToSample - .025) return 0;
    }
    return 1;
}

float AlpineLocalShadowVisibility(int index, float3 world)
{
    if (_AlpineLocalLightOptions[index].y < .5) return 1;
    // A requested shadow source fails closed if this camera did not retain
    // its real atlas slice. Neither direct Light nor its shadow budget changes.
    #if defined(BAR_PROMENADE_LOCAL_LIGHT_SHADOWS) && defined(_ADDITIONAL_LIGHT_SHADOWS)
        int lightIndex = (int)_AlpineLocalLightShadowIndices[index];
        if (lightIndex < 0) return 0;
        half4 shadow = GetAdditionalLightShadowParams(lightIndex);
        if (shadow.w < 0 || shadow.x <= 0) return 0;
        float3 toLight = _AlpineLocalLightPositions[index].xyz - world;
        return AdditionalLightRealtimeShadow(lightIndex, world,
            toLight * rsqrt(max(dot(toLight, toLight), .0001)));
    #else
        return 0;
    #endif
}

float AlpineLocalLightSupport(int index, float3 world)
{
    float3 margins = min(world - _AlpineLocalLightMinima[index].xyz,
        _AlpineLocalLightMaxima[index].xyz - world);
    float edge = saturate(min(margins.x, min(margins.y, margins.z)) / .35);
    float4 plane = _AlpineLocalLightPlanes[index];
    float distanceToFacade = dot(plane.xyz, world) + plane.w;
    if (dot(plane.xyz, plane.xyz) > .5)
        edge *= saturate(distanceToFacade / .20);
    int count = (int)_AlpineLocalLightOptions[index].z;
    [loop] for (int clip = 0; clip < count; clip++)
    {
        float4 boundary = _AlpineLocalLightClipPlanes[index * 6 + clip];
        if (dot(boundary.xyz, boundary.xyz) < .0001) continue;
        edge *= saturate((dot(boundary.xyz, world) + boundary.w) / .20);
    }
    return edge;
}

float3 AlpineLocalLightRadiance(int index, float3 world)
{
    float support = AlpineLocalLightSupport(index, world);
    [branch] if (support <= 0) return 0;
    [branch] if (AlpineLocalStructuralVisibility(index, world) <= 0) return 0;
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

half3 AlpineLocalParticleLighting(float3 world, bool sampleShadows)
{
    half3 result = 0;
    [loop] for (int index = 0; index < _AlpineLocalLightCount; index++)
    {
        float3 radiance = AlpineLocalLightRadiance(index, world);
        float peak = max(radiance.r, max(radiance.g, radiance.b));
        if (peak <= .00001) continue;
        float visibility = sampleShadows ? AlpineLocalShadowVisibility(index, world) : 1;
        // Snow remains a discrete moving reflector, with bounded radiance at
        // the source. Its original alpha, shape, soft depth and fog survive.
        result += radiance * (.55 / (1 + peak * .35)) *
            (_AlpineLocalLightOptions[index].x * visibility);
    }
    return result;
}
#endif
