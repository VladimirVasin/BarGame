"""Keep SSR smoothness and contact normals on the exact forward surface hooks."""


def build_depth_normals(source, family):
    ground = family == 'Ground'
    source = source.replace('#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"',
        '#include "ProjectScreenSpaceReflection.hlsl"\n#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"\n' +
        ('#include "GroundSurfaceLayers.hlsl"' if ground else
         '#include "WarehouseSurfaceLayers.hlsl"\n#include "WarehouseReflection.hlsl"'), 1)
    start = source.index('#if defined(_ALPHATEST_ON) || defined(_PARALLAXMAP)')
    end = source.index('#endif', start) + len('#endif')
    source = source[:start] + '#define REQUIRES_UV_INTERPOLATOR' + source[end:]
    metadata = ('    float4 groundRoad : TEXCOORD3;\n    half4 groundState : COLOR;'
        if ground else '    float2 warehouseMetres : TEXCOORD1;')
    source = source.replace('struct Attributes\n{', 'struct Attributes\n{\n' + metadata, 1)
    metadata = ('    float4 groundRoad : TEXCOORD7;\n    half4 groundState : TEXCOORD9;'
        if ground else '    float2 warehouseMetres : TEXCOORD7;')
    source = source.replace('struct Varyings\n{',
        'struct Varyings\n{\n    float3 surfaceWorld : TEXCOORD6;\n' + metadata, 1)
    transfer = ('    output.groundRoad = input.groundRoad;\n    output.groundState = input.groundState;'
        if ground else '    output.warehouseMetres = input.warehouseMetres;')
    source = source.replace('    output.normalWS = half3(normalInput.normalWS);',
        '    output.surfaceWorld = vertexInput.positionWS;\n' + transfer +
        '\n    output.normalWS = half3(normalInput.normalWS);', 1)
    if not ground:
        marker = '    #if defined(_PARALLAXMAP)\n'
        source = source.replace(marker,
            '    input.uv = WarehouseSurfaceUv(input.uv, input.warehouseMetres);\n\n' + marker, 1)
    old = '''    #if defined(_WRITE_SMOOTHNESS) && !defined(_SCREENSPACEREFLECTIONS_OFF)
        outNormalWS.a = SampleMetallicSpecGloss(input.uv, alpha).a;
    #endif'''
    if source.count(old) != 1:
        raise ValueError('Pinned URP DepthNormals smoothness hook changed')
    hook = '''    SurfaceData surface;
    InitializeStandardLitSurfaceData(input.uv, surface);
'''
    if ground:
        hook += '''    half relief;
    ApplyGroundLayers(input.uv, input.surfaceWorld, input.normalWS,
        input.groundRoad, input.groundState, surface, relief);
    half3 surfaceNormal = GroundReliefNormal(input.surfaceWorld,
        NormalizeNormalPerPixel(normalWS), relief);
    half receiver = GroundScreenSpaceReceiver();
'''
    else:
        hook += '''    half2 slope;
    ApplyWarehouseSurface(input.surfaceWorld, input.uv, surface, slope);
    half3 surfaceNormal = NormalizeNormalPerPixel(normalWS);
    ApplyWarehouseNormal(input.surfaceWorld, input.uv, slope, surfaceNormal);
    ApplyWarehouseWetSmoothness(input.surfaceWorld, surfaceNormal, surface);
    half receiver = WarehouseScreenSpaceReceiver(input.surfaceWorld, surfaceNormal);
'''
    hook += '''    #if defined(_GBUFFER_NORMALS_OCT)
        outNormalWS.rgb = PackFloat2To888(saturate(PackNormalOctQuadEncode(surfaceNormal) * .5 + .5));
    #else
        outNormalWS.rgb = surfaceNormal;
    #endif
    #if defined(_WRITE_SMOOTHNESS) && !defined(_SCREENSPACEREFLECTIONS_OFF)
        outNormalWS.a = receiver > .0001h ? surface.smoothness : 0;
    #endif'''
    return source.replace(old, hook, 1)
