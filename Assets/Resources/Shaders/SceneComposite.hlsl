#ifndef BARPROMENADE_SCENE_COMPOSITE_INCLUDED
#define BARPROMENADE_SCENE_COMPOSITE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

float4 _IntoxicationPixelSize;
float _SceneAspectFraction;
float _IntoxicationVignette;
float _IntoxicationGhostPixels;
float _IntoxicationWarp;
float _IntoxicationWarmth;
float _IntoxicationExposurePulse;
float _IntoxicationTime;
// (eye.x, eye.y, frame aspect, signed twist in radians) and
// (calm disc radius, radial pull, core drift in internal pixels).
float4 _IntoxicationVertigo;
float4 _IntoxicationVertigoShape;
// Colour envelope, edge pressure, optional lens separation, active seconds.
float4 _HawthornPulse;
TEXTURE2D_X(_HawthornSnapshot);
// Opacity, seconds since the held scene was captured, drift sign, cue seconds.
float4 _HawthornAfterimage;

// The drunk vertigo whirlpool. The frame is wound around the
// hero rather than around the middle of the screen: his own disc
// is left alone and only floats a couple of internal pixels,
// while everything outside it is twisted progressively harder,
// reaching the full angle at the frame corner farthest from him.
// Mirrored on the CPU by BarPromenade.Rendering.
// IntoxicationWhirlpool, which the contract test compares
// against this source.
float2 ApplyVertigoWhirlpool(float2 uv)
{
    float twist = _IntoxicationVertigo.w;
    float2 core = _IntoxicationVertigoShape.zw;
    if (twist == 0.0 && dot(core, core) == 0.0)
    {
        // Still water: the sober frame stays bit-exact, and the
        // boundary margin below never gets to zoom the picture.
        return uv;
    }

    float2 centre = _IntoxicationVertigo.xy;
    float aspect = _IntoxicationVertigo.z;
    float inner = _IntoxicationVertigoShape.x;
    float pull = _IntoxicationVertigoShape.y;

    float2 p = (uv - centre) * float2(aspect, 1.0);
    float radius = length(p);
    float2 far = float2(
        max(centre.x, 1.0 - centre.x) * aspect,
        max(centre.y, 1.0 - centre.y));
    float outer = max(length(far), inner + 0.001);
    float t = saturate(
        (radius - inner) /
        max(0.001, outer - inner));
    float profile = t * t * (3.0 - 2.0 * t);

    float sine;
    float cosine;
    sincos(twist * profile, sine, cosine);
    float2 rotated = float2(
        p.x * cosine - p.y * sine,
        p.x * sine + p.y * cosine);
    rotated *= 1.0 - pull * profile;

    float2 delta = rotated / float2(aspect, 1.0);
    delta +=
        core *
        _IntoxicationPixelSize.xy *
        saturate(1.0 - radius / inner);
    // The radius is shortened to the frame's own boundary along
    // the twisted direction, so the smear points into the vortex
    // instead of becoming an axis-aligned clamp at the edge.
    float2 wall = float2(
        delta.x >= 0.0 ? 0.999 - centre.x : centre.x - 0.001,
        delta.y >= 0.0 ? 0.999 - centre.y : centre.y - 0.001);
    float2 inverse = 1.0 / (abs(delta) + 1e-6);
    float scale = min(
        1.0,
        min(wall.x * inverse.x, wall.y * inverse.y));
    return centre + delta * scale;
}

float4 SampleSceneComposite(float2 targetUv, bool applySwallowGrade)
{
    float2 centered = targetUv - 0.5;
    float wave =
        sin(
            targetUv.y * 13.0 +
            _IntoxicationTime * 1.15) +
        sin(
            targetUv.x * 8.0 -
            _IntoxicationTime * 0.73) *
        0.35;
    float radiusSquared = dot(centered, centered);
    // In 4:3 mode the internal frame reads only the
    // centered 4:3 window of the widescreen source — the
    // exact view of a 4:3 camera with the same vertical
    // FOV. Warp, vignette and rain stay in target space,
    // so they follow the visible frame; the whirlpool is
    // wound in that same target space, before the crop.
    float2 sourceUv = ApplyVertigoWhirlpool(targetUv);
    sourceUv.x =
        0.5 +
        (sourceUv.x - 0.5) * _SceneAspectFraction;
    float2 warpedUv =
        sourceUv +
        float2(wave, wave * -0.22) *
        _IntoxicationWarp +
        centered *
        radiusSquared *
        _IntoxicationWarp *
        1.8;
    float4 source = SAMPLE_TEXTURE2D_X_LOD(
        _BlitTexture, sampler_LinearClamp, warpedUv, 0.0);

    float2 ghostOffset =
        _IntoxicationPixelSize.xy *
        _IntoxicationGhostPixels *
        float2(
            sin(_IntoxicationTime * 0.83),
            cos(_IntoxicationTime * 1.07) * 0.55);
    // Reuse the existing samples. The short cue has its own clock, so it
    // still travels when the ordinary intoxication profile is stationary.
    ghostOffset += _IntoxicationPixelSize.xy * _HawthornPulse.z *
        float2(.85, -.25 + .35 * sin(_HawthornPulse.w * 3.2));
    float4 ghost =
        SAMPLE_TEXTURE2D_X_LOD(
            _BlitTexture,
            sampler_LinearClamp,
            warpedUv + ghostOffset,
            0.0);
    float ghostWeight =
        saturate(max(_IntoxicationGhostPixels / 3.0, _HawthornPulse.z * .65));
    source.rgb = lerp(
        source.rgb,
        (source.rgb + ghost.rgb) * 0.5,
        ghostWeight * 0.38);
    float redSample =
        SAMPLE_TEXTURE2D_X_LOD(
            _BlitTexture,
            sampler_LinearClamp,
            warpedUv + ghostOffset * 0.7,
            0.0).r;
    float blueSample =
        SAMPLE_TEXTURE2D_X_LOD(
            _BlitTexture,
            sampler_LinearClamp,
            warpedUv - ghostOffset * 0.7,
            0.0).b;
    source.r = lerp(
        source.r,
        redSample,
        ghostWeight * 0.42);
    source.b = lerp(
        source.b,
        blueSample,
        ghostWeight * 0.42);

    source.rgb *=
        1.0 +
        sin(_IntoxicationTime * 0.85) *
        _IntoxicationExposurePulse;
    source.rgb = lerp(
        source.rgb,
        source.rgb * float3(1.08, 1.01, 0.94),
        saturate(_IntoxicationWarmth));
    float edge =
        saturate((length(centered) - 0.2) / 0.48);
    source.rgb *=
        1.0 -
        edge *
        edge *
        saturate(_IntoxicationVignette);

    [branch] if (applySwallowGrade && _HawthornAfterimage.x > 0.0)
    {
        float age = saturate(_HawthornAfterimage.y / .48);
        float sign = _HawthornAfterimage.z;
        float2 drift = float2(sign * .017, -.0055) * age;
        float2 smear = float2(sign * .008, .0025) * age;
        float2 heldUv = (sourceUv - .5) * (1.0 - .018 * age) + .5 - drift;
        // All three taps read a genuinely frozen earlier frame, not the live
        // image. Its finite travelling smear grows between periodic captures.
        float3 held = SAMPLE_TEXTURE2D_X_LOD(_HawthornSnapshot,
            sampler_LinearClamp, heldUv, 0.0).rgb * .55;
        held += SAMPLE_TEXTURE2D_X_LOD(_HawthornSnapshot,
            sampler_LinearClamp, heldUv - smear, 0.0).rgb * .30;
        held += SAMPLE_TEXTURE2D_X_LOD(_HawthornSnapshot,
            sampler_LinearClamp, heldUv - smear * 2.0, 0.0).rgb * .15;
        float rememberedLight = smoothstep(.012, .10, dot(held, float3(.2126, .7152, .0722)));
        float perimeter = smoothstep(.10, .55, length(centered));
        float opacity = _HawthornAfterimage.x * rememberedLight * lerp(.65, 1.0, perimeter);
        source.rgb = lerp(source.rgb, held * float3(1.0, .92, .95), opacity);
    }

    [branch] if (applySwallowGrade && _HawthornPulse.x > 0.0)
    {
        float cueTime = _HawthornPulse.w;
        float2 pressureCentre = centered - float2(-.055 + cueTime * .045, .025);
        float perimeter = smoothstep(.14, .57,
            length(pressureCentre * float2(.88, 1.0)));
        // A single broad, slightly diagonal wave crosses the frame. It is a
        // colour variation, not a flashing overlay or a new source of light.
        float travellingBand = 1.0 - smoothstep(.10, .38,
            abs(targetUv.x + targetUv.y * .16 - (-.16 + cueTime * 1.15)));
        float brightest = max(source.r, max(source.g, source.b));
        float luminance = dot(source.rgb, float3(.2126, .7152, .0722));
        float3 crimson = lerp(float3(.57, .055, .25), float3(1.0, .035, .105),
            smoothstep(.008, .12, luminance));
        crimson = lerp(crimson, float3(1.0, .32, .105),
            smoothstep(.28, .85, luminance));
        float3 graded = source.rgb * .16 + brightest * crimson * .84;
        // Pure black remains pure black, and fine shading remains in the
        // original signal. The centre clears before the remaining edge tint.
        float weight = saturate(_HawthornPulse.x *
            (.68 + perimeter * .20 + travellingBand * _HawthornPulse.y * .12));
        source.rgb = lerp(source.rgb, graded, weight);
        source.rgb *= 1.0 - perimeter * perimeter * _HawthornPulse.y * .12;
    }

    return source;
}

#endif
