#!/usr/bin/env python3
"""Retain complete project Lit/URP passes while adding bounded warehouse wear sampling.

Only the Forward surface hook differs. All passes use the same extended
UnityPerMaterial layout, preserving SRP batching. --check detects an upstream
project Lit/URP change; no package or shared shader is modified.
"""
import argparse
from pathlib import Path
import uuid
from surface_depth_normals import build_depth_normals

ROOT = Path(__file__).resolve().parents[1]
SHADERS = ROOT / "Assets/Resources/Shaders"


def once(source, old, new):
    if source.count(old) != 1:
        raise ValueError("Warehouse shader upstream hook changed: " + old[:100])
    return source.replace(old, new, 1)


def outputs():
    packages = list((ROOT / "Library/PackageCache").glob(
        "com.unity.render-pipelines.universal@*/Shaders/LitInput.hlsl"))
    if len(packages) != 1:
        raise ValueError("Expected one imported URP LitInput source")
    source_folder = packages[0].parent
    shader = (SHADERS / "Lit.shader").read_text(encoding="utf-8")
    # Warehouse supplies its own surface UV/roughness hooks and brush frame.
    # Both paths reuse DirectionalMetalLighting, never stack forward wrappers.
    shader = shader.replace('            #include "ProjectLitDirectionalLighting.hlsl"\n', '')
    shader = shader.replace('            #define InitializeStandardLitSurfaceData ProjectLitSurfaceData\n', '')
    shader = shader.replace('            #undef InitializeStandardLitSurfaceData\n', '')
    shader = shader.replace('#define UniversalFragmentPBR ProjectLitFragmentPBR',
                            '#define UniversalFragmentPBR WarehouseFragmentPBR')
    shader = shader[shader.index('Shader "'):]
    shader = once(shader, 'Shader "Bar Promenade/Lit"',
                  'Shader "Bar Promenade/Warehouse Surface"')
    shader = once(shader, '    Properties\n    {', '''    Properties
    {
        _WarehouseMaterialResponse("Unique slopes / roughness / cavity", 2D) = "gray" {}
        _WarehouseResponseStrength("Physical material response", Range(0,1)) = 0
        _WarehouseNormalStrength("Near material relief", Range(0,1)) = 0
        _WarehouseAnisotropy("Directional metal response", Range(0,0.55)) = 0
        _WarehouseWetReflection("Local wet reflection", Range(0,1)) = 0
        _WarehouseAuxRect("Bounded auxiliary surface patch", Vector) = (1,1,0,0)
        _WarehouseAuxiliary("Auxiliary surface coordinates", Float) = 0''')
    shader = shader.replace('#pragma target 2.0', '#pragma target 3.5', 1)
    shader = shader.replace(
        '#include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"',
        '#include "WarehouseSurfaceInput.hlsl"')
    shader = once(shader,
        '#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"',
        '#include "WarehouseSurfaceForwardPass.hlsl"')
    shader = once(shader,
        '#include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"',
        '#include "WarehouseSurfaceDepthNormalsPass.hlsl"')
    depth_start = shader.index('Name "DepthNormals"')
    shader = shader[:depth_start] + shader[depth_start:].replace('#pragma target 2.0', '#pragma target 3.5', 1)
    shader = once(shader,
        '#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"',
        '#include "WarehouseDirectionalLighting.hlsl"')
    shader_input = packages[0].read_text(encoding="utf-8")
    shader_input = once(shader_input, 'UNITY_TEXTURE_STREAMING_DEBUG_VARS;\nCBUFFER_END', '''UNITY_TEXTURE_STREAMING_DEBUG_VARS;
half _WarehouseResponseStrength;
half _WarehouseNormalStrength;
half _WarehouseAnisotropy;
half _WarehouseWetReflection;
float4 _WarehouseAuxRect;
half _WarehouseAuxiliary;
CBUFFER_END''')
    forward = (source_folder / "LitForwardPass.hlsl").read_text(encoding="utf-8")
    forward = forward.replace('"LitInput.hlsl"', '"WarehouseSurfaceInput.hlsl"')
    forward = once(forward, 'struct Attributes\n{',
                   '#include "WarehouseSurfaceLayers.hlsl"\n#include "WarehouseReflection.hlsl"\n\nstruct Attributes\n{')
    forward = once(forward, '    float2 uv                       : TEXCOORD0;',
                   '    float2 uv                       : TEXCOORD0;\n    float2 warehouseMetres          : TEXCOORD11;')
    forward = once(forward, '    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);',
                   '    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);\n    output.warehouseMetres = input.staticLightmapUV;')
    forward = once(forward, '    InitializeStandardLitSurfaceData(input.uv, surfaceData);',
        '''    float2 warehouseSurfaceUv = WarehouseSurfaceUv(input.uv, input.warehouseMetres);
    InitializeStandardLitSurfaceData(warehouseSurfaceUv, surfaceData);
    half2 warehouseSlope;
    ApplyWarehouseSurface(input.positionWS, warehouseSurfaceUv, surfaceData, warehouseSlope);''')
    forward = once(forward, '    InitializeInputData(input, surfaceData.normalTS, inputData);',
        '''    InitializeInputData(input, surfaceData.normalTS, inputData);
    ApplyWarehouseNormal(input.positionWS, warehouseSurfaceUv, warehouseSlope, inputData.normalWS);''')
    forward = once(forward, '    half4 color = UniversalFragmentPBR(inputData, surfaceData);',
        '''    ApplyWarehouseWetSmoothness(input.positionWS, inputData.normalWS, surfaceData);
    projectScreenSpaceReceiver = WarehouseScreenSpaceReceiver(input.positionWS, inputData.normalWS);
    WarehouseSetBrushFrame(input.positionWS, warehouseSurfaceUv, inputData.normalWS);
    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    color.rgb += WarehouseWetReflection(input.positionWS, inputData.normalWS,
        inputData.viewDirectionWS, surfaceData);''')
    header = '// Generated by tools/build-combat-warehouse-shader.py; edit its hooks or WarehouseSurfaceLayers.hlsl.\n'
    depth = build_depth_normals((source_folder / 'LitDepthNormalsPass.hlsl').read_text(encoding='utf-8'), 'Warehouse')
    return {SHADERS / name: header + '\n'.join(line.rstrip() for line in value.splitlines()) + '\n'
            for name, value in {
                'WarehouseSurface.shader': shader,
                'WarehouseSurfaceInput.hlsl': shader_input,
                'WarehouseSurfaceForwardPass.hlsl': forward,
                'WarehouseSurfaceDepthNormalsPass.hlsl': depth}.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    for path, value in outputs().items():
        if args.check:
            if not path.exists() or path.read_text(encoding='utf-8') != value:
                raise ValueError('Regenerate warehouse shader: ' + str(path))
        else:
            path.write_text(value, encoding='utf-8', newline='\n')
            meta = Path(str(path) + '.meta')
            if not meta.exists():
                importer = 'ShaderImporter' if path.suffix == '.shader' else 'ShaderIncludeImporter'
                guid = uuid.uuid5(uuid.NAMESPACE_URL, path.relative_to(ROOT).as_posix()).hex
                meta.write_text(f'fileFormatVersion: 2\nguid: {guid}\n{importer}:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n', encoding='utf-8')
    print('Warehouse shader hooks retain the upstream project Lit/URP passes and material layout.')


if __name__ == '__main__':
    main()
