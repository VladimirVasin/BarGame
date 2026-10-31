Shader "BarPromenade/WorldControlOutline"
{
    Properties
    {
        [HideInInspector] _ControlCenterOS("Control Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ControlExtentsOS("Control Extents", Vector) = (1, 1, 1, 0)
        [HideInInspector] _OutlineWidth("Pixel Width", Float) = 2
        [HideInInspector] _OverlayColor("Selection Color", Color) = (0.88, 0.84, 0.73, 1)
        [HideInInspector] _Cull("Cull", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+20" "RenderType"="Opaque" }
        Pass
        {
            Name "ControlContour"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ControlCenterOS;
                float4 _ControlExtentsOS;
                float4 _OverlayColor;
                float _OutlineWidth;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                if (_OutlineWidth > 0)
                {
                    // Imported hard-face duplicates must move identically.
                    // Bounds-normalised radial directions preserve a thin
                    // handle's side edge without readable meshes or CPU copies.
                    float3 radialOS = (input.positionOS.xyz - _ControlCenterOS.xyz) / max(_ControlExtentsOS.xyz, 0.000001);
                    float3 radialVS = TransformWorldToViewDir(TransformObjectToWorldDir(radialOS));
                    float4 centreCS = TransformObjectToHClip(_ControlCenterOS.xyz);
                    float2 radial = output.positionCS.xy / max(output.positionCS.w, 0.0001) -
                        centreCS.xy / max(centreCS.w, 0.0001);
                    float2 direction = dot(radialVS.xy, radialVS.xy) > 0.0001 ? radialVS.xy : radial;
                    direction /= max(length(direction), 0.0001);
                    output.positionCS.xy += direction * (2.0 * _OutlineWidth / _ScaledScreenParams.xy) * output.positionCS.w;
                }
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _OverlayColor; }
            ENDHLSL
        }
    }
}
