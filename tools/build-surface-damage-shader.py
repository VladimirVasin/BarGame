#!/usr/bin/env python3
"""Derive complete project Lit/URP passes around opt-in object damage masks.

Only authored macro albedo/smoothness changes. Base UVs, normal maps, opacity,
geometry, lighting, stencil and all depth/shadow/motion passes stay upstream.
Forward, GBuffer and Meta use the same single clamp-atlas sample and material
layout. No source package is edited; --check detects upstream hook drift.
"""
import argparse
import json
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[1]
SHADERS = ROOT / "Assets/Resources/Shaders"

PROPERTIES = '''
        [NoScaleOffset] _SurfaceDamageAtlas("Unique object damage masks", 2D) = "black" {}
        _SurfaceDamageRect("Damage chart x/y/width/height", Vector) = (0,0,1,1)
        _SurfaceDamageBoundsMin("Damage domain minimum", Vector) = (0,0,0,0)
        _SurfaceDamageInvSize("Damage domain inverse size", Vector) = (1,1,1,0)
        _SurfaceDamageMode("Strength / projection", Vector) = (0,0,0,0)
        _SurfaceDamageSoil("Linear soil multiplier", Color) = (1,1,1,1)
        _SurfaceDamageExpose("Linear substrate target", Color) = (0.25,0.25,0.25,1)
        _SurfaceDamageAccent("Linear accent target", Color) = (0.10,0.10,0.10,1)
        _SurfaceDamageResponse("Smoothness scale", Vector) = (0.66,0,0,0)
        // _SurfaceDamageObjectToDomain is a matrix supplied by the owner MPB.
'''

FIELDS = '''#ifndef BAR_PROMENADE_SURFACE_DAMAGE_FIELDS_INCLUDED
#define BAR_PROMENADE_SURFACE_DAMAGE_FIELDS_INCLUDED
// Include INSIDE UnityPerMaterial in every pass, including non-colour passes.
float4 _SurfaceDamageRect;
float4 _SurfaceDamageBoundsMin;
float4 _SurfaceDamageInvSize;
float4x4 _SurfaceDamageObjectToDomain;
float4 _SurfaceDamageMode;
half4 _SurfaceDamageSoil;
half4 _SurfaceDamageExpose;
half4 _SurfaceDamageAccent;
half4 _SurfaceDamageResponse;
#endif
'''

COMMON = '''#ifndef BAR_PROMENADE_SURFACE_DAMAGE_COMMON_INCLUDED
#define BAR_PROMENADE_SURFACE_DAMAGE_COMMON_INCLUDED
// Callers declare SurfaceDamageFields inside their shared material CBUFFER.
TEXTURE2D(_SurfaceDamageAtlas); SAMPLER(sampler_SurfaceDamageAtlas);

float2 SurfaceDamageChartUv(float3 positionOS, float3 normalOS)
{
    float3 domain = mul(_SurfaceDamageObjectToDomain, float4(positionOS, 1.0)).xyz;
    float3 p = saturate((domain - _SurfaceDamageBoundsMin.xyz) * _SurfaceDamageInvSize.xyz);
    if (_SurfaceDamageMode.y < 0.5) return p.xy;
    if (_SurfaceDamageMode.y < 1.5) return p.zy;
    if (_SurfaceDamageMode.y < 2.5) return p.xz;

    // One authored 3x2 chart: bottom +X/-X/+Y, top -Y/+Z/-Z.
    // The chart contract explicitly forbids flips or repeating macro UVs.
    float3 a = abs(normalOS);
    float face;
    float2 plane;
    if (a.x >= a.y && a.x >= a.z)
    {
        face = normalOS.x >= 0.0 ? 0.0 : 1.0;
        plane = p.zy;
    }
    else if (a.y >= a.z)
    {
        face = normalOS.y >= 0.0 ? 2.0 : 3.0;
        plane = p.xz;
    }
    else
    {
        face = normalOS.z >= 0.0 ? 4.0 : 5.0;
        plane = p.xy;
    }
    float row = floor(face / 3.0);
    float column = face - row * 3.0;
    return (float2(column, row) + plane) / float2(3.0, 2.0);
}

void ApplySurfaceDamage(float3 positionOS, float3 normalOS, inout SurfaceData surface)
{
    [branch] if (_SurfaceDamageMode.x <= 0.0) return;
    float2 chart = saturate(SurfaceDamageChartUv(positionOS, normalOS));
    float2 uv = _SurfaceDamageRect.xy + chart * _SurfaceDamageRect.zw;
    half4 masks = SAMPLE_TEXTURE2D(_SurfaceDamageAtlas, sampler_SurfaceDamageAtlas, uv);
    masks *= saturate(_SurfaceDamageMode.x);

    // Targets are supplied in linear space. Only restrained original micro
    // contrast remains inside exposed/coloured patches; macro masks never tile.
    half microLuma = dot(surface.albedo, half3(0.2126h, 0.7152h, 0.0722h));
    half microVariation = clamp(0.92h + 0.16h * microLuma, 0.92h, 1.08h);
    surface.albedo *= lerp(half3(1.0h, 1.0h, 1.0h), _SurfaceDamageSoil.rgb, masks.r);
    surface.albedo = lerp(surface.albedo, _SurfaceDamageExpose.rgb * microVariation, masks.g);
    surface.albedo = lerp(surface.albedo, _SurfaceDamageAccent.rgb * microVariation, masks.b);
    surface.smoothness *= lerp(1.0h, saturate(_SurfaceDamageResponse.x), masks.a);
    // Alpha, normals, metallic, emission and geometry retain their own owners.
}
#endif
'''

WORLD_POSITION = '''
// Damage domains are authored in the original object frame for every variant.
#ifndef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
#define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
#endif
'''


def once(source, old, new):
    if source.count(old) != 1:
        raise ValueError("Surface damage upstream hook changed: " + old[:120])
    return source.replace(old, new, 1)


def outputs():
    packages = list((ROOT / "Library/PackageCache").glob(
        "com.unity.render-pipelines.universal@*/Shaders/LitInput.hlsl"))
    if len(packages) != 1:
        raise ValueError("Expected one imported URP LitInput source")
    shaders = packages[0].parent
    package = json.loads((shaders.parent / "package.json").read_text(encoding="utf-8"))
    if package.get("version") != "17.6.0":
        raise ValueError("Review surface damage shader hooks against the imported URP version")
    shader = (SHADERS / "Lit.shader").read_text(encoding="utf-8")
    shader = shader[shader.index('Shader "'):]
    shader = once(shader, 'Shader "Bar Promenade/Lit"',
                  'Shader "Bar Promenade/Surface Damage Lit"')
    shader = once(shader, '    Properties\n    {', '    Properties\n    {' + PROPERTIES.rstrip())
    # Preserve the original targets. Raising Forward from 2.0 to 3.5 also opts
    # Metal into UNITY_SUPPORT_INSTANCING; owner-specific chart/matrix uniforms
    # are not instanced data. The shared runtime material opts out explicitly.
    shader = shader.replace(
        '#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"',
        '#include "SurfaceDamageInput.hlsl"')
    for source, target in (
            ("LitForwardPass.hlsl", "SurfaceDamageForwardPass.hlsl"),
            ("LitGBufferPass.hlsl", "SurfaceDamageGBufferPass.hlsl"),
            ("LitMetaPass.hlsl", "SurfaceDamageMetaPass.hlsl")):
        shader = once(shader,
            '#include "Packages/com.unity.render-pipelines.universal/Shaders/' + source + '"',
            '#include "' + target + '"')

    shader_input = once(packages[0].read_text(encoding="utf-8"),
        'UNITY_TEXTURE_STREAMING_DEBUG_VARS;\nCBUFFER_END',
        'UNITY_TEXTURE_STREAMING_DEBUG_VARS;\n#include "SurfaceDamageFields.hlsl"\nCBUFFER_END')

    forward = (shaders / "LitForwardPass.hlsl").read_text(encoding="utf-8")
    forward = once(forward, '#include "LitInput.hlsl"',
        '#include "SurfaceDamageInput.hlsl"\n#include "SurfaceDamageCommon.hlsl"' + WORLD_POSITION)
    forward = once(forward, '    InitializeStandardLitSurfaceData(input.uv, surfaceData);',
        '''    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    ApplySurfaceDamage(TransformWorldToObject(input.positionWS),
        TransformWorldToObjectNormal(input.normalWS), surfaceData);''')

    gbuffer = (shaders / "LitGBufferPass.hlsl").read_text(encoding="utf-8")
    gbuffer = once(gbuffer,
        '#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"',
        '#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"\n'
        '#include "SurfaceDamageCommon.hlsl"' + WORLD_POSITION)
    gbuffer = once(gbuffer, '    InitializeStandardLitSurfaceData(input.uv, surfaceData);',
        '''    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    ApplySurfaceDamage(TransformWorldToObject(input.positionWS),
        TransformWorldToObjectNormal(input.normalWS), surfaceData);''')

    meta_vertex = (shaders.parent / "ShaderLibrary/UniversalMetaPass.hlsl").read_text(encoding="utf-8")
    meta_vertex = once(meta_vertex, '    float2 uv           : TEXCOORD0;',
        '''    float2 uv           : TEXCOORD0;
    float3 damagePositionOS : TEXCOORD3;
    float3 damageNormalOS : TEXCOORD4;''')
    meta_vertex = once(meta_vertex, '    output.uv = TRANSFORM_TEX(input.uv0, _BaseMap);',
        '''    output.uv = TRANSFORM_TEX(input.uv0, _BaseMap);
    output.damagePositionOS = input.positionOS.xyz;
    output.damageNormalOS = input.normalOS;''')
    meta = (shaders / "LitMetaPass.hlsl").read_text(encoding="utf-8")
    meta = once(meta,
        '#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"',
        '#include "SurfaceDamageUniversalMetaPass.hlsl"\n#include "SurfaceDamageCommon.hlsl"')
    meta = once(meta, '    InitializeStandardLitSurfaceData(input.uv, surfaceData);',
        '''    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    ApplySurfaceDamage(input.damagePositionOS, input.damageNormalOS, surfaceData);''')

    header = '// Generated by tools/build-surface-damage-shader.py; retain complete project Lit/URP contracts.\n'
    return {SHADERS / name: header + '\n'.join(line.rstrip() for line in source.splitlines()) + '\n'
        for name, source in {
            "SurfaceDamageLit.shader": shader,
            "SurfaceDamageInput.hlsl": shader_input,
            "SurfaceDamageFields.hlsl": FIELDS,
            "SurfaceDamageCommon.hlsl": COMMON,
            "SurfaceDamageForwardPass.hlsl": forward,
            "SurfaceDamageGBufferPass.hlsl": gbuffer,
            "SurfaceDamageUniversalMetaPass.hlsl": meta_vertex,
            "SurfaceDamageMetaPass.hlsl": meta}.items()}


def validate_receiver():
    dither = (SHADERS / "HomeOccluderDither.shader").read_text(encoding="utf-8")
    for required in ('#include "SurfaceDamageFields.hlsl"',
            '#include "SurfaceDamageCommon.hlsl"',
            'ApplySurfaceDamage(TransformWorldToObject(input.positionWS),'):
        if dither.count(required) != 1:
            raise ValueError("Home dither must retain its opt-in shared damage receiver: " + required)


def validate_contract(generated):
    shader = generated[SHADERS / "SurfaceDamageLit.shader"]
    baseline = (SHADERS / "Lit.shader").read_text(encoding="utf-8")
    # Keep every original pass and its original shared Lit material input. The
    # colour hooks alter only SurfaceData after the upstream initializer.
    import re
    if re.findall(r'Name "([^"]+)"', shader) != re.findall(r'Name "([^"]+)"', baseline):
        raise ValueError("Surface damage must preserve the complete project Lit pass set")
    if re.findall(r'#pragma target ([^\n]+)', shader) != re.findall(
            r'#pragma target ([^\n]+)', baseline):
        raise ValueError("Surface damage must preserve the original pass targets and instancing support")
    if shader.count('#include "SurfaceDamageInput.hlsl"') != baseline.count(
            '#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"'):
        raise ValueError("Every pass must use the same surface damage material layout")
    for name in ("Forward", "GBuffer", "Meta"):
        source = generated[SHADERS / ("SurfaceDamage" + name + "Pass.hlsl")]
        if source.count("    ApplySurfaceDamage(") != 1:
            raise ValueError(name + " must apply the shared damage operation exactly once")
    common = generated[SHADERS / "SurfaceDamageCommon.hlsl"]
    if common.count("SAMPLE_TEXTURE2D(") != 1 or re.search(r"\bfrac\s*\(", common):
        raise ValueError("Damage requires one atlas sample and may never repeat macro UVs")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    generated = outputs()
    validate_contract(generated)
    validate_receiver()
    for path, value in generated.items():
        if args.check:
            if not path.exists() or path.read_text(encoding='utf-8') != value:
                raise ValueError("Regenerate object damage shader: " + str(path))
        else:
            with path.open('w', encoding='utf-8', newline='\n') as output:
                output.write(value)
            meta = Path(str(path) + '.meta')
            if not meta.exists():
                importer = 'ShaderImporter' if path.suffix == '.shader' else 'ShaderIncludeImporter'
                guid = uuid.uuid5(uuid.NAMESPACE_URL, path.relative_to(ROOT).as_posix()).hex
                meta.write_text(f'fileFormatVersion: 2\nguid: {guid}\n{importer}:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n', encoding='utf-8')
    print("SURFACE DAMAGE SHADER CONTRACT OK: one clamp mask sample, Forward/GBuffer/Meta parity, shared material layout and opt-in dither receiver")


if __name__ == '__main__':
    main()
